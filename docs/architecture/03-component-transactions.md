# 03 — Component Diagram: Transactions Service (C4 Level 3)

## Visão Geral

O **Transactions Service** é responsável por registrar e consultar lançamentos financeiros (débitos e créditos). Este diagrama detalha os **componentes internos** do serviço, suas responsabilidades e como se relacionam.

A arquitetura segue um modelo **Clean Architecture simplificado** com três camadas:
- **API Layer** — Endpoints, validação, serialização
- **Application Layer** — Orquestração de regras de negócio
- **Infrastructure Layer** — Persistência e mensageria

---

## Diagrama

```mermaid
C4Component
    title Transactions Service — Component Diagram (C4 Level 3)

    Container_Boundary(transapi, "Transactions API (.NET 8)") {

        Component(endpoints, "TransactionEndpoints", "Minimal API", "Define rotas HTTP:\n• POST /api/v1/transactions\n• GET /api/v1/transactions\n• GET /api/v1/transactions/{id}\nMapeia request → command → response")

        Component(validator, "TransactionValidator", "FluentValidation", "Valida regras de negócio:\n• amount > 0\n• description obrigatório (≤500 chars)\n• category: enum válido\n• date não pode ser futura")

        Component(service, "TransactionService", "Application Service", "Orquestra o fluxo de criação:\n1. Chama validator\n2. Cria domain entity\n3. Persiste via repository\n4. Insere no outbox\n5. Retorna resultado")

        Component(domain, "Transaction", "Domain Aggregate", "Entidade raiz do aggregate:\n• Id (ObjectId)\n• Type (DEBIT|CREDIT)\n• Amount (decimal)\n• Description (string)\n• Category (enum)\n• Date (DateOnly)\n• CreatedAt / UpdatedAt\nRegras de negócio encapsuladas")

        Component(category, "Category", "Domain Value Object", "Enum de categorias válidas:\nSales, Services, Supplies,\nUtilities, Returns, Other")

        Component(itxrepo, "ITransactionRepository", "Interface", "Abstração de persistência:\n• InsertAsync(transaction)\n• GetByIdAsync(id)\n• GetByPeriodAsync(from, to, type)\n• ExistsAsync(id)")

        Component(txrepo, "MongoTransactionRepository", "Infrastructure", "Implementação MongoDB:\n• Collection: transactions\n• Usa MongoDB driver\n• Index: date + type\n• Decimal128 para amount")

        Component(ioutbox, "IOutboxRepository", "Interface", "Abstração de outbox:\n• InsertAsync(outboxMessage)\n• GetPendingAsync(batchSize)\n• DeleteAsync(id)")

        Component(outboxrepo, "MongoOutboxRepository", "Infrastructure", "Implementação MongoDB:\n• Collection: outbox\n• Armazena eventos pendentes\n• Status: PENDING → PUBLISHED")

        Component(outboxpub, "OutboxPublisher", "Background Service", "Worker que publica eventos:\n1. A cada 1s busca eventos pendentes\n2. Publica cada evento no RabbitMQ\n3. Marca como PUBLISHED no outbox\nGarante at-least-once delivery")

        Component(rabbitclient, "RabbitMQClient", "Infrastructure", "Wrapper de conexão RabbitMQ:\n• Gerencia canal e conexão\n• Retry com backoff\n• Serialização de mensagens\n• Exchange: events\nRouting key: transaction.created")
    }

    ContainerDb(mongodb, "MongoDB", "transactions_db", "Coleções:\n• transactions\n• outbox")

    Container_Ext(rabbitmq, "RabbitMQ", "Message Broker", "Exchange: events\nQueue: transaction.created")

    Rel(endpoints, validator, "Valida input com")
    Rel(endpoints, service, "Delega para")
    Rel(service, domain, "Instancia")
    Rel(service, itxrepo, "Persiste via")
    Rel(service, ioutbox, "Insere evento via")
    Rel(itxrepo, txrepo, "Implementado por")
    Rel(ioutbox, outboxrepo, "Implementado por")
    Rel(txrepo, mongodb, "Lê/escreve", "MongoDB driver")
    Rel(outboxrepo, mongodb, "Lê/escreve", "MongoDB driver")
    Rel(outboxpub, ioutbox, "Busca eventos pendentes via")
    Rel(outboxpub, rabbitclient, "Publica mensagens com")
    Rel(rabbitclient, rabbitmq, "AMQP publish", "AMQP 0-9-1")
```

---

## Descrição dos Componentes

### API Layer

#### TransactionEndpoints
**Responsabilidade:** Definir e expor rotas HTTP do serviço

- Tecnologia: .NET 8 Minimal APIs (`app.MapPost`, `app.MapGet`)
- Realiza: parsing de request → validação básica → delegação ao service
- Serialização: `System.Text.Json` (AOT-ready)
- Resposta: `IResult` com status codes corretos (201, 400, 404, 401, 500)
- Autenticação: middleware valida JWT antes de chegar ao endpoint

**Rotas:**
```
POST   /api/v1/transactions            → criar lançamento
GET    /api/v1/transactions            → listar por período
GET    /api/v1/transactions/{id}       → detalhe de uma transação
GET    /health                         → health check
GET    /metrics                        → Prometheus metrics
```

---

### Application Layer

#### TransactionValidator
**Responsabilidade:** Validar regras de negócio de input

Tecnologia: FluentValidation

| Campo | Regra | Erro |
|-------|-------|------|
| `amount` | > 0 e não nulo | "Amount must be greater than zero" |
| `type` | DEBIT ou CREDIT | "Transaction type must be DEBIT or CREDIT" |
| `description` | Não vazio, ≤ 500 chars | "Description is required (max 500 chars)" |
| `category` | Enum válido | "Category must be one of: Sales, Services..." |
| `date` | ≤ hoje (sem futuro) | "Transaction date cannot be in the future" |

#### TransactionService
**Responsabilidade:** Orquestrar o registro de lançamento como uma intenção atômica de negócio

O registro de um lançamento é tratado como uma **intenção indivisível**: ou confirma completamente (lançamento registrado + notificação garantida ao serviço de consolidação), ou reverte completamente, sem deixar rastro. Não existe estado intermediário.

```
INTENÇÃO ATÔMICA — Registrar Lançamento:
  1. Validar input → se inválido, rejeita imediatamente (nada persiste)
  2. Construir o lançamento com as regras de domínio
  3. Registrar o lançamento + garantir a notificação de forma indivisível
     → Se qualquer parte falhar: estado anterior é preservado por completo
  4. Confirmar o registro e retornar resultado ao solicitante

A notificação ao serviço de consolidação é parte integrante da intenção,
não um efeito colateral opcional. Um lançamento só é considerado registrado
quando a notificação também está garantida.
```

---

### Domain Layer

#### Transaction (Aggregate Root)
**Responsabilidade:** Encapsular regras de negócio de um lançamento

```csharp
// Estrutura conceitual:
class Transaction {
    ObjectId Id
    string UserId               // Extraído do JWT — quem criou o lançamento (imutável)
    TransactionType Type        // DEBIT | CREDIT
    decimal Amount              // Sempre positivo
    string Description          // Obrigatório, ≤ 500 chars
    Category Category           // Enum
    DateOnly Date               // Sem data futura
    DateTime CreatedAt
    DateTime UpdatedAt
    string Status               // PENDING | CONFIRMED
}
```

> **Nota de Segurança:** `UserId` nunca é aceito como input do cliente. É
> extraído do JWT pelo middleware de autenticação e injetado no comando antes
> de chegar ao aggregate. Ver [ADR-003](../../decisions/ADR-003-user-context-propagation.md).

**Regras encapsuladas:**
- Amount sempre positivo (débito/crédito é pelo `Type`)
- Date nunca pode ser futura
- Imutabilidade: transações com data > 24h não podem ser alteradas

#### Category (Value Object)
Enum de categorias válidas:
- `Sales` — Vendas
- `Services` — Serviços prestados
- `Supplies` — Compra de insumos
- `Utilities` — Utilidades (luz, água, etc.)
- `Returns` — Devoluções
- `Other` — Outros

---

### Infrastructure Layer

#### MongoTransactionRepository
**Responsabilidade:** Persistir e consultar transações no MongoDB

- Collection: `transactions_db.transactions`
- Índices:
  - `date` + `type` (composto) — otimiza consultas por período e tipo
  - `_id` — padrão ObjectId
- Operações:
  - `InsertAsync` — aceita `IClientSessionHandle` (para transação)
  - `GetByIdAsync` — busca por ObjectId
  - `GetByPeriodAsync` — filtro por data + tipo + paginação
- Tipo para `amount`: `Decimal128` (precisão financeira)

#### MongoOutboxRepository
**Responsabilidade:** Gerenciar eventos aguardando publicação

- Collection: `transactions_db.outbox`
- Estrutura do documento:
  ```
  {
    _id: ObjectId,
    eventType: "TransactionCreated",
    payload: { ... },
    status: "PENDING" | "PUBLISHED",
    createdAt: DateTime,
    processedAt: DateTime?
  }
  ```
- Operações:
  - `InsertAsync` — aceita `IClientSessionHandle` (mesma transação)
  - `GetPendingAsync(batchSize)` — busca eventos PENDING em lote
  - `MarkPublishedAsync(id)` — atualiza status para PUBLISHED

#### OutboxPublisher (Background Service)
**Responsabilidade:** Publicar eventos pendentes para o RabbitMQ

**Ciclo:**
```
LOOP a cada 1 segundo:
  1. Buscar até 50 eventos com status PENDING
  2. Para cada evento:
     a. PUBLISH no RabbitMQ
     b. Se sucesso → MARK como PUBLISHED
     c. Se falha → retry com backoff (mantém PENDING)
  3. Aguardar 1 segundo
  4. Repetir
```

**Garantia:** Se o processo reiniciar, os eventos PENDING serão reprocessados — at-least-once delivery.

#### RabbitMQClient
**Responsabilidade:** Abstração de conexão e publicação

- Gerencia pool de conexões e canais
- Retry automático em caso de falha de conexão
- Serialização para JSON (UTF-8)
- Exchange: `events` (type: topic)
- Routing key: `transaction.created`

---

## Fluxos de Sequência

### Fluxo 1: Criar Lançamento (Happy Path)

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as TransactionEndpoints
    participant VAL as TransactionValidator
    participant SVC as TransactionService
    participant TXREPO as MongoTransactionRepository
    participant OUTBOX as MongoOutboxRepository
    participant PUB as OutboxPublisher
    participant MQ as RabbitMQ

    Merchant->>GW: POST /api/v1/transactions (JWT)
    GW->>GW: Valida JWT + Rate limit
    GW->>EP: Encaminha requisição

    EP->>VAL: Valida input (amount, type, etc.)
    VAL-->>EP: ✅ Válido

    EP->>SVC: CreateTransactionAsync(command)

    SVC->>SVC: Instancia Transaction domain entity

    Note over SVC,OUTBOX: MongoDB Transaction (Atomicidade)
    SVC->>TXREPO: InsertAsync(transaction, session)
    TXREPO-->>SVC: ✅ Inserido

    SVC->>OUTBOX: InsertAsync(outboxEvent, session)
    OUTBOX-->>SVC: ✅ Inserido

    Note over SVC: COMMIT MongoDB session

    SVC-->>EP: TransactionDto
    EP-->>GW: 201 Created
    GW-->>Merchant: 201 Created { id, type, amount, ... }

    Note over PUB,MQ: Processamento assíncrono (background)
    PUB->>OUTBOX: GetPendingAsync(batchSize=50)
    OUTBOX-->>PUB: [OutboxEvent]
    PUB->>MQ: Publish(TransactionCreated)
    MQ-->>PUB: ✅ Confirmado
    PUB->>OUTBOX: MarkPublishedAsync(id)
```

---

### Fluxo 2: Criar Lançamento (Validação Falha)

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as TransactionEndpoints
    participant VAL as TransactionValidator

    Merchant->>GW: POST /api/v1/transactions (amount: -50)
    GW->>GW: Valida JWT ✅
    GW->>EP: Encaminha requisição

    EP->>VAL: Valida input
    VAL-->>EP: ❌ "Amount must be greater than zero"

    EP-->>GW: 400 Bad Request
    GW-->>Merchant: 400 Bad Request { errors: [...] }

    Note over EP: Nenhum dado persistido
    Note over EP: Nenhum evento publicado
```

---

### Fluxo 3: Consultar Transações por Período

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as TransactionEndpoints
    participant SVC as TransactionService
    participant REPO as MongoTransactionRepository
    participant DB as MongoDB

    Merchant->>GW: GET /api/v1/transactions?startDate=2024-03-01&endDate=2024-03-31
    GW->>GW: Valida JWT + Rate limit
    GW->>EP: Encaminha requisição

    EP->>SVC: GetByPeriodAsync(startDate, endDate, page, pageSize)

    SVC->>REPO: GetByPeriodAsync(filter)
    REPO->>DB: find({ date: { $gte, $lte } }).skip().limit()
    DB-->>REPO: [Transaction]
    REPO-->>SVC: PagedResult<Transaction>

    SVC-->>EP: PagedResult<TransactionDto>
    EP-->>GW: 200 OK
    GW-->>Merchant: 200 OK { page, total, data: [...] }
```

---

## Padrões Aplicados

### Intenção Atômica de Negócio

O registro de um lançamento é tratado como uma **intenção única e indivisível**. Não existe confirmação parcial.

```
┌──────────────────────────────────────────────────────────────────┐
│              REGISTRAR LANÇAMENTO = INTENÇÃO ÚNICA               │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Validar ──► Registrar ──► Garantir Notificação ──► Confirmar   │
│                                                                  │
├──────────────────────────────────────────────────────────────────┤
│  SUCESSO → Lançamento registrado + notificação garantida         │
│  FALHA   → Nada persiste. Nada notifica. Estado preservado.      │
└──────────────────────────────────────────────────────────────────┘
```

| Cenário | Resultado |
|---------|-----------|
| Validação falha | Rejeição imediata — nenhum dado alterado |
| Persistência falha | Rollback completo — nenhuma notificação gerada |
| Notificação falha | Rollback completo — lançamento não confirmado |
| Tudo ok | Confirmação — lançamento existe + notificação garantida |

### Outbox Pattern

Garante que o registro do lançamento e o registro da notificação ocorrem como **uma única unidade de trabalho**: ou ambos confirmam, ou nenhum confirma.

```
SEM garantia de atomicidade:
  Registra lançamento → ✅
  Notifica consolidação → ❌ falha de rede
  Resultado: lançamento existe, consolidado nunca atualiza ← INCONSISTÊNCIA

COM garantia de atomicidade (Outbox Pattern):
  [Registra lançamento + Registra notificação pendente] → ✅ ou ❌ juntos
  Mecanismo de retransmissão entrega notificação ao broker → eventual ✅
  Resultado: consistência garantida mesmo em falhas transitórias
```

- ✅ Falha de rede no broker → notificação permanece pendente para retry
- ✅ Processo reinicia → notificações pendentes são reprocessadas
- ✅ Lançamento nunca existe sem notificação correspondente garantida

### Separação de Responsabilidades

| Componente | Responsabilidade | NÃO faz |
|-----------|-----------------|---------|
| `TransactionEndpoints` | Entrada HTTP — recebe e devolve | Regras de negócio |
| `TransactionValidator` | Validação de regras de input | Persistência |
| `TransactionService` | Orquestração da intenção atômica | Detalhes de infraestrutura |
| `Transaction` | Regras e invariantes de domínio | Persistência |
| `MongoTransactionRepository` | Persistência do lançamento | Regras de negócio |
| `OutboxPublisher` | Retransmissão confiável ao broker | Lógica de negócio |

---

**Próximo documento:** `docs/architecture/04-component-consolidation.md` (C4 Level 3 — Consolidation Service + Worker)
