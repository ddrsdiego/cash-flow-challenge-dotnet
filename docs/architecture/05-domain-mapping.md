# Domain Mapping — CashFlow System

## Visão Geral

O **CashFlow System** é dividido em dois bounded contexts (domínios separados) que trabalham juntos para permitir que um comerciante registre e consolide seu fluxo de caixa. Este documento define:

1. **Bounded Contexts** — Domínios independentes com seu próprio modelo de dados
2. **Domain Events** — Eventos que comunicam mudanças entre contextos
3. **Linguagem Ubíqua** — Glossário de termos do domínio
4. **Context Map** — Como os contextos se relacionam

---

## Bounded Contexts

### Context 1: TRANSACTIONS (Lançamentos)

#### Responsabilidade
Registrar e gerenciar transações financeiras individuais (débitos e créditos) do comerciante.

#### Operações Principais
- Criar lançamento (débito ou crédito)
- Consultar histórico de lançamentos
- Validar lançamentos
- Persistir transações

#### Modelo de Dados
```
Transaction (Entidade Agregadora)
├── id: UUID
├── type: "DEBIT" | "CREDIT"
├── amount: Decimal (sempre positivo)
├── description: String
├── category: Category Enum
├── date: Date
├── createdAt: Timestamp
├── updatedAt: Timestamp
└── status: "PENDING" | "CONFIRMED"

Category Enum
├── Sales
├── Services
├── Supplies
├── Utilities
├── Returns
└── Other
```

#### Regras de Negócio
- Transaction.amount > 0 (sempre)
- Transaction.description é obrigatório e não vazio
- Transaction.date não pode ser > hoje
- Transações com data > 24h passadas são imutáveis (somente leitura)
- Cada lançamento é independente (não afeta outros)

#### Events Publicados
- `TransactionCreated` — Novo lançamento registrado
  ```json
  {
    "eventId": "uuid",
    "eventType": "TransactionCreated",
    "aggregateId": "transaction-id",
    "aggregateType": "Transaction",
    "data": {
      "transactionId": "507f1f77bcf86cd799439011",
      "type": "CREDIT",
      "amount": 500.00,
      "description": "Venda do dia",
      "category": "Sales",
      "date": "2024-03-15",
      "createdAt": "2024-03-15T15:30:00Z"
    },
    "timestamp": "2024-03-15T15:30:00Z"
  }
  ```

#### Idioma do Domínio (Linguagem Ubíqua)
- **Lançamento** = Transaction (registo de entrada/saída de dinheiro)
- **Débito** = DEBIT (despesa, saída)
- **Crédito** = CREDIT (receita, entrada)
- **Categoria** = agrupamento semântico (Sales, Services, Supplies)

---

### Context 2: CONSOLIDATION (Consolidado Diário)

#### Responsabilidade
Agregar transações diárias e calcular saldo consolidado (total de créditos - total de débitos).

#### Operações Principais
- Receber eventos de novas transações
- Recalcular saldo diário
- Consultar saldo consolidado
- Invalidar cache de consolidação

#### Modelo de Dados
```
DailyConsolidation (Entidade Agregadora)
├── id: UUID
├── date: Date (chave única)
├── totalCredits: Decimal
├── totalDebits: Decimal
├── balance: Decimal (calculado: totalCredits - totalDebits)
├── transactionCount: Int
├── lastUpdated: Timestamp
└── status: "CURRENT" | "ARCHIVED"

ConsolidationSummary (Value Object - para cache)
├── date: Date
├── totalCredits: Decimal
├── totalDebits: Decimal
├── balance: Decimal
└── cachedAt: Timestamp
```

#### Regras de Negócio
- Um dia só pode ter UM documento de consolidação
- DailyConsolidation.balance = SUM(credits) - SUM(debits)
- Precisão: 2 casas decimais (centavos)
- Consolidação é **somente leitura** para o usuário (recalculada via eventos)
- Consolidado só é recalculado quando há novo evento TransactionCreated
- Cache (Redis) tem TTL de 5 minutos

#### Events Consumidos
- `TransactionCreated` — Nova transação registrada
  - **Ação:** Recalcular saldo para aquele dia
  - **Idempotência:** Usar idempotencyKey para evitar duplicação

#### Events Publicados
- `DailyConsolidationCalculated` — Saldo diário foi recalculado
  ```json
  {
    "eventId": "uuid",
    "eventType": "DailyConsolidationCalculated",
    "aggregateId": "consolidation-{date}",
    "aggregateType": "DailyConsolidation",
    "data": {
      "date": "2024-03-15",
      "totalCredits": 1500.00,
      "totalDebits": 800.50,
      "balance": 699.50,
      "transactionCount": 12
    },
    "timestamp": "2024-03-15T18:45:30Z"
  }
  ```

#### Idioma do Domínio (Linguagem Ubíqua)
- **Consolidado** = DailyConsolidation (saldo agregado de um dia)
- **Saldo** = Balance (resultado: créditos - débitos)
- **Saldo Diário** = saldo de um dia específico

---

## Domain Events (Eventos de Domínio)

### Padrão Event Sourcing Light
Não mantemos histórico de todos os eventos, mas os eventos são o meio de comunicação entre contextos.

### Event: TransactionCreated

**Quando ocorre:** Imediatamente após inserção bem-sucedida em Transactions DB  
**Quem publica:** Transactions Service  
**Quem consome:** Consolidation Worker

**Estrutura:**
```json
{
  "eventId": "{{uuid}}",
  "eventType": "TransactionCreated",
  "version": "1.0",
  "aggregateId": "{{transaction-id}}",
  "aggregateType": "Transaction",
  "timestamp": "2024-03-15T15:30:00Z",
  "idempotencyKey": "{{uuid}}",
  "data": {
    "transactionId": "507f1f77bcf86cd799439011",
    "type": "CREDIT",
    "amount": 500.00,
    "description": "Venda do dia",
    "category": "Sales",
    "date": "2024-03-15"
  }
}
```

**Fluxo:**
```
1. Transactions API insere em transactions_db
2. Transactions API insere em outbox (mesmo documento ou collection separada)
3. Transação MongoDB: COMMIT
4. Outbox Publisher lê evento do outbox
5. Publica em RabbitMQ (exchange: `events`, routing key: `transaction.created`)
6. Remove do outbox
7. Consolidation Worker consome da fila
8. Recalcula saldo para o dia
9. Atualiza consolidation_db
10. Invalida cache (Redis)
```

### Event: DailyConsolidationCalculated

**Quando ocorre:** Após recalcular saldo diário  
**Quem publica:** Consolidation Worker  
**Quem consome:** (futuro) Notificações, webhooks, auditoria

**Estrutura:**
```json
{
  "eventId": "{{uuid}}",
  "eventType": "DailyConsolidationCalculated",
  "version": "1.0",
  "aggregateId": "consolidation-2024-03-15",
  "aggregateType": "DailyConsolidation",
  "timestamp": "2024-03-15T18:45:30Z",
  "data": {
    "date": "2024-03-15",
    "totalCredits": 1500.00,
    "totalDebits": 800.50,
    "balance": 699.50,
    "transactionCount": 12
  }
}
```

---

## Linguagem Ubíqua (Glossário de Domínio)

| Termo | Definição | Exemplo | Contexto |
|-------|-----------|---------|----------|
| **Lançamento** | Registro de uma entrada ou saída de dinheiro | R$ 100 de venda | Transactions |
| **Débito** | Lançamento de saída (despesa/uso) | R$ 50 em insumos | Transactions |
| **Crédito** | Lançamento de entrada (receita/venda) | R$ 200 em vendas | Transactions |
| **Transação** | Sinônimo de lançamento (termos intercambiáveis) | Uma venda registrada | Transactions |
| **Categoria** | Agrupamento semântico de transações | "Sales", "Supplies" | Transactions |
| **Saldo** | Resultado: total de créditos - total de débitos | R$ 699,50 | Consolidation |
| **Consolidado** | Relatório agregado de transações de um dia | Resumo de 15/03 | Consolidation |
| **Saldo Diário** | Saldo consolidado de um dia específico | Saldo em 15/03 | Consolidation |
| **Fluxo de Caixa** | Movimento de dinheiro (entradas e saídas) | Histórico mensal | Geral |
| **Comerciante** | Pessoa ou empresa que usa o sistema | João Silva (vendedor) | Geral |
| **Períod**o | Intervalo de tempo (dia, mês, trimestre) | 01/03 a 31/03 | Geral |

---

## Context Map (Relação Entre Contextos)

### Diagrama Visual

```
┌─────────────────────────────────────────────────────────────┐
│                    CASHFLOW SYSTEM                          │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌──────────────────────┐        ┌──────────────────────┐  │
│  │   TRANSACTIONS       │        │   CONSOLIDATION      │  │
│  │   (Lançamentos)      │        │   (Saldo Diário)     │  │
│  │                      │        │                      │  │
│  │ • Criar lançamento   │        │ • Receber eventos    │  │
│  │ • Validar dados      │        │ • Recalcular saldo   │  │
│  │ • Persistir trans    │        │ • Cache saldo        │  │
│  │ • Publicar evento    │        │ • Consultar saldo    │  │
│  │                      │        │                      │  │
│  └──────────┬───────────┘        └──────────┬───────────┘  │
│             │                               │               │
│             │ TransactionCreated            │               │
│             │ (evento via RabbitMQ)         │               │
│             │──────────────────────────────▶│               │
│             │                               │               │
│             │                DailyConsolidation             │
│             │                Calculated                     │
│             │◀──────────────────────────────│               │
│                                                             │
│  PADRÃO: Publish-Subscribe (Event-Driven)                  │
│  ACOPLAMENTO: Loosely coupled (Consolidation não          │
│               conhece Transactions)                        │
│  COMUNICAÇÃO: Assíncrona (via RabbitMQ)                   │
│  GARANTIA: At-least-once (com idempotência)               │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Tipo de Relação: Published Language + Subscription

**Published Language (Linguagem Publicada):**
- Transactions publica eventos em linguagem bem definida
- Event structure é contrato entre contextos
- Mudanças na estrutura de evento = versão nova (backward compatible)

**Subscription:**
- Consolidation Worker se inscreve em `transaction.created` queue
- Processa eventos de forma independente
- Não há chamada síncrona entre contextos

**Benefício:** Isolamento total
- Transactions nunca falha por culpa de Consolidation
- Consolidation pode estar down e Transactions continua operando
- Fila absorve picos de carga

---

## Capacidades de Negócio por Contexto

### TRANSACTIONS Context

```
┌─────────────────────────────────────────┐
│  Transaction Service                    │
│                                         │
│  Capacidade 1: Ingestão de Transações  │
│  ├─ POST /transactions                 │
│  ├─ Validação em tempo real            │
│  └─ Resposta imediata (< 200ms)        │
│                                         │
│  Capacidade 2: Consulta de Histórico   │
│  ├─ GET /transactions?period=...       │
│  ├─ Filtros por tipo, categoria, data  │
│  └─ Paginação de resultados            │
│                                         │
│  Capacidade 3: Auditoria               │
│  ├─ Imutabilidade (transações > 24h)  │
│  ├─ Logging de todas criações/leituras │
│  └─ Rastreamento via traceId           │
│                                         │
└─────────────────────────────────────────┘
```

### CONSOLIDATION Context

```
┌──────────────────────────────────────────┐
│  Consolidation Service + Worker          │
│                                          │
│  Capacidade 1: Processamento Assíncrono │
│  ├─ Worker consome TransactionCreated   │
│  ├─ Recalcula saldo em background       │
│  └─ Retry com exponential backoff       │
│                                          │
│  Capacidade 2: Consulta de Saldo        │
│  ├─ GET /consolidation/daily?date=...  │
│  ├─ Cache em Redis (5min TTL)           │
│  └─ Fallback para DB se miss            │
│                                          │
│  Capacidade 3: Agregação e Análise      │
│  ├─ Cálculo: balance = credits - debits │
│  ├─ Contagem de transações              │
│  └─ Timestamp da última atualização     │
│                                          │
└──────────────────────────────────────────┘
```

---

## Fluxos de Integração Entre Contextos

### Fluxo 1: Criar Transação (Happy Path)

```
SEQUÊNCIA CRONOLÓGICA:

1. Comerciante
   ▼
   POST /api/transactions
   ├─ type: CREDIT
   ├─ amount: 500.00
   └─ date: 2024-03-15

2. Transactions API
   ▼
   ├─ Valida input
   ├─ Valida autenticação
   ├─ BEGIN TRANSACTION
   ├─ INSERT transactions_db
   ├─ INSERT outbox (event)
   ├─ COMMIT
   └─ RETURN 201 Created

3. Outbox Publisher (background)
   ▼
   ├─ Lê evento do outbox
   ├─ Publica em RabbitMQ
   │  (exchange: events, routing_key: transaction.created)
   └─ DELETE outbox

4. RabbitMQ
   ▼
   └─ Fila: consolidation-worker.input

5. Consolidation Worker
   ▼
   ├─ Consome mensagem
   ├─ Idempotency check (já processado?)
   ├─ Busca transações para 2024-03-15
   ├─ Calcula:
   │  ├─ totalCredits = SUM(type=CREDIT)
   │  ├─ totalDebits = SUM(type=DEBIT)
   │  └─ balance = totalCredits - totalDebits
   ├─ UPSERT consolidation_db
   ├─ DELETE consolidation-redis:{date} (cache)
   ├─ Marca como processado
   └─ ACK mensagem

6. Consolidation API
   ▼
   GET /api/consolidation/daily?date=2024-03-15
   ├─ Busca cache (miss)
   ├─ Busca consolidation_db
   ├─ STORE em cache (5min)
   └─ RETURN { totalCredits, totalDebits, balance }

TIMELINE:
T0 → T0+20ms   = Transação criada (Transactions API)
T0+50ms        = Evento publicado em RabbitMQ
T0+100ms       = Consolidation Worker consome e processa
T0+150ms       = Consolidado recalculado
T0+200ms       = Cache atualizado
```

### Fluxo 2: Cenário de Falha (Consolidation Worker Down)

```
1. Transactions API
   └─ Cria transação normalmente
      └─ Publica evento em RabbitMQ

2. RabbitMQ
   └─ Evento fica na fila (consolidation-worker.input)

3. Consolidation Worker
   └─ ❌ DOWN (no responding)

4. Resultado
   ├─ Transactions: 100% operacional
   │  └─ Novos lançamentos continuam sendo registrados
   │
   ├─ Consolidation API: Retorna dados VELHOS
   │  └─ GET /consolidation/daily → última versão no DB (pode estar com 1h desatualizada)
   │
   └─ RabbitMQ: Acumula mensagens
      └─ Quando worker volta → processa backlog

5. Quando Consolidation Worker volta
   ├─ Consome todas as mensagens na fila (ordem preservada)
   ├─ Idempotência: ignora duplicatas
   └─ Consolidado fica atualizado em segundos
```

---

## Princípios de Design

### 1. Isolamento de Contextos
- Cada contexto tem seu próprio database
- Sem joins entre databases
- Sem chamadas síncronas diretas

### 2. Comunicação Assíncrona
- Todos os eventos passam por RabbitMQ
- Sem timeouts de requisição
- Consolidation nunca bloqueia Transactions

### 3. Idempotência Obrigatória
- Mesmo evento pode ser processado 2x por erro de network
- Worker rastreia `idempotencyKey` para evitar duplicação
- Operação precisa ser segura se executada múltiplas vezes

### 4. Consistência Eventual
- Transação criada = salva imediatamente
- Consolidado = calculado em segundos/minutos
- Normal ter lag entre criação e consolidação

### 5. Resiliência por Design
- Transactions não depende de Consolidation estar up
- Consolidation degrada graciosamente (mostra dados velhos)
- Nenhum contexto é SPOF (single point of failure)

---

## Mapeamento Futuro

### Possíveis Novos Contextos (fora do MVP)

```
┌──────────────────────────────────────────┐
│  Future: ANALYTICS Context               │
│  • Relatórios customizáveis              │
│  • Trends e previsões                    │
│  │ (consome DailyConsolidationCalculated)│
│  └─► publica AnalyticsReportGenerated   │
└──────────────────────────────────────────┘

┌──────────────────────────────────────────┐
│  Future: NOTIFICATIONS Context           │
│  • Alertas de transações                 │
│  • Notificações de consolidação          │
│  │ (consome TransactionCreated)          │
│  │ (consome DailyConsolidationCalculated)│
│  └─► publica NotificationSent            │
└──────────────────────────────────────────┘

┌──────────────────────────────────────────┐
│  Future: RECONCILIATION Context          │
│  • Conciliação com banco                 │
│  • Validação de discrepâncias            │
│  │ (consome DailyConsolidationCalculated)│
│  └─► publica ReconciliationCompleted     │
└──────────────────────────────────────────┘
```

---

## Conclusão

O **CashFlow System** é arquitetado com dois bounded contexts altamente desacoplados:

1. **Transactions Context** — Responsável por ingestão e armazenamento de transações
2. **Consolidation Context** — Responsável por agregação e consulta de saldos

A comunicação via **Published Language** e **Event-Driven Architecture** garante que:
- ✅ Resiliência total (um falha não afeta o outro)
- ✅ Escalabilidade independente (cada contexto escala conforme sua carga)
- ✅ Manutenibilidade (lógica separada, fácil de entender)
- ✅ Extensibilidade (novos contextos podem se inscrever em eventos existentes)

Este design permite que o sistema cresça de um MVP simples para uma plataforma financeira robusta.
