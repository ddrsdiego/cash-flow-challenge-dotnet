# Requisitos Funcionais

## Visão Geral

O **CashFlow System** é um sistema de controle de fluxo de caixa que permite a um comerciante registrar e consolidar suas operações financeiras. O sistema é composto por dois serviços desacoplados:

1. **Transactions Service** — Registra débitos (despesas) e créditos (receitas)
2. **Consolidation Service** — Gera relatórios consolidados diários com saldo

## Atores e Personas

### Ator Primário: Comerciante
- **Descrição:** Proprietário ou gestor financeiro de um comercial
- **Interações principais:**
  - Registrar novos lançamentos (débitos/créditos)
  - Consultar saldo consolidado diário
  - Visualizar histórico de transações

### Ator Secundário: Sistema Externo (Futuro)
- Possível integração com sistemas de ERP, contabilidade, etc.

---

## Casos de Uso Principais

### UC-01: Criar Lançamento de Débito
**Ator:** Comerciante  
**Descrição:** Registrar uma despesa (saída de dinheiro) no fluxo de caixa

**Pré-condições:**
- Comerciante autenticado
- Comerciante possui papel `admin` (per ADR-009)

**Fluxo Principal:**
1. Comerciante acessa a API de Transações: `POST /api/transactions` (com JWT no header Authorization)
2. Fornece os dados:
   - `type`: "DEBIT" (despesa)
   - `amount`: valor em decimal positivo (ex: 150.50)
   - `description`: descrição da despesa (ex: "Compra de insumos")
   - `date`: data da transação (YYYY-MM-DD)
   - `category`: categoria (ex: "Supplies", "Services", "Utilities")
   - ⚠️ **`userId` NÃO é enviado pelo cliente** — é extraído automaticamente do claim `sub` do JWT
3. Sistema valida os dados
4. Sistema extrai `userId` do JWT e associa ao lançamento
5. Sistema persiste o lançamento no Transactions DB (MongoDB), incluindo o `userId`
6. Sistema publica evento `TransactionCreated` no RabbitMQ (incluindo `userId` para rastreabilidade)
7. Sistema retorna o lançamento criado com `201 Created`

**Fluxos Alternativos:**

**FA-01.1: Dados inválidos**
- Passo 3: Validação falha (amount ≤ 0, description vazia, etc.)
- Sistema retorna `400 Bad Request` com detalhes do erro
- Lançamento NÃO é criado

**FA-01.2: Acesso não autorizado**
- Passo 1: Usuário não possui token válido ou permissão `transactions:write`
- Sistema retorna `401 Unauthorized` ou `403 Forbidden`
- Lançamento NÃO é criado

**FA-01.3: Serviço indisponível**
- Passo 4: Database conectar falha
- Sistema retorna `503 Service Unavailable`
- Cliente implementa retry
- Lançamento é persisted quando serviço volta

**Pós-condições:**
- Lançamento armazenado em `transactions_db.transactions`
- Evento `TransactionCreated` publicado em `transaction.created` queue
- Saldo do consolidado será recalculado (assincrono)

---

### UC-02: Criar Lançamento de Crédito
**Ator:** Comerciante  
**Descrição:** Registrar uma receita (entrada de dinheiro) no fluxo de caixa

**Pré-condições:**
- Comerciante autenticado
- Comerciante possui papel `admin` (per ADR-009)

**Fluxo Principal:**
1. Comerciante acessa a API de Transações: `POST /api/transactions` (com JWT no header Authorization)
2. Fornece os dados:
   - `type`: "CREDIT" (receita)
   - `amount`: valor em decimal positivo (ex: 500.00)
   - `description`: descrição da receita (ex: "Vendas do dia")
   - `date`: data da transação (YYYY-MM-DD)
   - `category`: categoria (ex: "Sales", "Services", "Returns")
   - ⚠️ **`userId` NÃO é enviado pelo cliente** — é extraído automaticamente do claim `sub` do JWT
3. Sistema valida os dados
4. Sistema extrai `userId` do JWT e associa ao lançamento
5. Sistema persiste o lançamento no Transactions DB (MongoDB), incluindo o `userId`
6. Sistema publica evento `TransactionCreated` no RabbitMQ (incluindo `userId` para rastreabilidade)
7. Sistema retorna o lançamento criado com `201 Created`

**Fluxos Alternativos:** Idênticos a UC-01

**Pós-condições:**
- Lançamento armazenado em `transactions_db.transactions`
- Evento `TransactionCreated` publicado

---

### UC-03: Consultar Consolidado Diário
**Ator:** Comerciante  
**Descrição:** Obter o saldo consolidado (débitos + créditos) para um dia específico

**Pré-condições:**
- Comerciante autenticado
- Comerciante possui papel `admin` ou `user` (per ADR-009)

**Fluxo Principal:**
1. Comerciante acessa a API de Consolidação via Gateway: `GET /api/v1/consolidation/{date}` (ex: `/api/v1/consolidation/2024-03-15`)
2. Gateway roteia para Consolidation API em `/consolidation/{date}`
3. Sistema recebe a data solicitada
4. Sistema busca o consolidado em **cache IMemoryCache** primeiro
5. Se HIT em cache: retorna resultado imediatamente (< 10ms)
6. Se MISS em cache:
   - Busca em `consolidation_db.daily_balances`
   - Calcula agregações se necessário: `sum(credits) - sum(debits) = balance`
   - Armazena em IMemoryCache por 5 minutos (TTL)
   - Retorna resultado
6. Sistema retorna `200 OK` com:
   ```json
   {
     "date": "2024-03-15",
     "totalCredits": 1500.00,
     "totalDebits": 800.50,
     "balance": 699.50,
     "transactionCount": 12,
     "lastUpdated": "2024-03-15T18:45:30Z"
   }
   ```

**Fluxos Alternativos:**

**FA-03.1: Data não encontrada**
- Passo 3-5: Nenhum dado de consolidação para a data
- Sistema retorna `404 Not Found`
- OU retorna saldo zero com flag `noTransactions: true`

**FA-03.2: Data futura**
- Passo 2: Data > hoje
- Sistema retorna `400 Bad Request` - "Cannot query future dates"

**FA-03.3: Acesso não autorizado**
- Passo 1: Token inválido ou permissão `consolidation:read` ausente
- Sistema retorna `401` ou `403`

**Pós-condições:**
- Consolidado retornado ao cliente
- Cache atualizado (se miss anterior)
- Nenhuma alteração de estado

---

### UC-04: Listar Lançamentos por Período
**Ator:** Comerciante  
**Descrição:** Consultar histórico de transações em um período (ex: últimos 30 dias)

**Pré-condições:**
- Comerciante autenticado
- Comerciante possui permissão de leitura (role: `transactions:read`)

**Fluxo Principal:**
1. Comerciante acessa: `GET /api/transactions?startDate=2024-02-15&endDate=2024-03-15&type=CREDIT`
2. Sistema valida parâmetros:
   - `startDate` <= `endDate`
   - Intervalo máximo: 90 dias
3. Sistema busca em `transactions_db.transactions` com filtros
4. Sistema retorna lista paginada (padrão: 20 por página):
   ```json
   {
     "page": 1,
     "pageSize": 20,
     "total": 150,
     "data": [
       {
         "id": "507f1f77bcf86cd799439011",
         "userId": "user-123",
         "type": "CREDIT",
         "amount": 500.00,
         "description": "Venda do dia",
         "category": "Sales",
         "date": "2024-03-15",
         "createdAt": "2024-03-15T15:30:00Z"
       },
       ...
     ]
   }
   ```

**Fluxos Alternativos:**

**FA-04.1: Intervalo muito grande**
- Passo 2: `endDate - startDate > 90 dias`
- Sistema retorna `400 Bad Request` - "Maximum interval is 90 days"

**FA-04.2: Sem resultados**
- Passo 3: Nenhuma transação no intervalo
- Sistema retorna `200 OK` com array vazio

**Pós-condições:**
- Lista de transações retornada ao cliente
- Sem alteração de estado

---

## Regras de Negócio

### RN-01: Validação de Valor
- **Regra:** Amount sempre positivo (>0)
- **Contexto:** Débito e crédito são diferenciados pelo campo `type`, nunca pelo sinal do amount
- **Exemplo:** Débito de R$100 = `{ type: "DEBIT", amount: 100.00 }`

### RN-02: Cálculo de Saldo
- **Regra:** `Balance = Sum(Credits) - Sum(Debits)`
- **Precisão:** 2 casas decimais (centavos)
- **Contexto:** Apenas para dados consolidados, não afeta transações individuais

### RN-03: Imutabilidade de Transações Passadas
- **Regra:** Transações com data no passado (> 24h) não podem ser alteradas/deletadas
- **Justificativa:** Auditoria e compliance financeiro
- **Ação:** Deletar/editar lançamento passado retorna `410 Gone` ou `403 Forbidden`

### RN-04: Consolidação Diária Única
- **Regra:** Um dia só pode ter UM documento de consolidação
- **Contexto:** Se transação for criada em 15/03, consolidado de 15/03 é recalculado (não duplicado)
- **Implementação:** Upsert em `consolidation_db`, não insert

### RN-05: Descrição Obrigatória e Não Vazia
- **Regra:** `description` é obrigatório e não pode estar vazio ou conter apenas espaços
- **Validação:** `!string.IsNullOrWhiteSpace(description) && description.Length <= 500`

### RN-06: Categoria Vinculada
- **Regra:** Transação deve estar vinculada a uma categoria válida
- **Categorias permitidas:** Configurável, mas deve ter lista predefinida
- **Exemplo:** ["Sales", "Services", "Supplies", "Utilities", "Returns", "Other"]
- **Validação:** Enum/lista de valores permitidos

### RN-07: Isolamento de Falhas
- **Regra:** Se Consolidation Service falhar, Transactions Service continua operacional
- **Implementação:** Comunicação assíncrona (não síncrona)
- **Implicação:** Consolidado pode estar temporariamente desatualizado enquanto Transactions está 100% funcional

### RN-08: Identidade do Usuário por Extração, não por Input
- **Regra:** O `userId` associado a cada lançamento é extraído do JWT — jamais aceito como campo no body da requisição
- **Justificativa:** Impede que um usuário crie lançamentos em nome de outro
- **Implementação:** API Gateway propaga `userId` via header `X-User-Id` extraído do claim `sub` do JWT; Transactions API lê esse header ao criar o lançamento
- **Imutabilidade:** `userId` não pode ser alterado após a criação do lançamento
- **Auditoria:** `userId` é persistido em cada `Transaction` e propagado no evento `TransactionCreated` para rastreabilidade futura
- **Escopo no MVP:** `userId` identifica o autor do lançamento para auditoria; **não isola dados** entre usuários (single-tenant MVP)
- **Ver:** [ADR-003](../decisions/ADR-003-user-context-propagation.md)

---

## Fluxos de Integração Entre Serviços

### Fluxo 1: Criar Transação → Atualizar Consolidado

```
┌─────────────────────┐
│  Comerciante        │
│  POST /transaction  │
└──────────┬──────────┘
           │
           v
┌─────────────────────┐
│  Transactions API   │
│  • Valida dados     │
│  • Persiste no DB   │
│  • Publica evento   │
└──────────┬──────────┘
           │
           │ TransactionCreated Event
           │ (RabbitMQ: transaction.created)
           │
           v
┌─────────────────────┐
│  Consolidation      │
│  Worker            │
│  • Consome evento   │
│  • Recalcula saldo  │
│  • Invalida cache   │
│  • Atualiza DB      │
└─────────────────────┘

⚡ Importante: Worker falha => Transactions continua operacional
   (mensagem fica em DLQ para retry posterior)
```

### Fluxo 2: Consultar Consolidado

```
┌──────────────────────────┐
│  Comerciante             │
│  GET /consolidation/date │
└──────────┬───────────────┘
           │
           v
┌──────────────────────────┐
│  Consolidation API       │
│  • Busca em cache        │ ← IMemoryCache (5min TTL, per-instance)
│    (se hit: retorna)     │
│  • Se miss: busca DB     │ ← MongoDB
│  • Armazena em cache     │
│  • Retorna resultado     │
└──────────────────────────┘
```

---

## Endpoints Resumo (API REST)

### Transactions Service
| Método | Endpoint | Descrição | Status |
|--------|----------|-----------|--------|
| POST | `/api/transactions` | Criar débito/crédito | 201/400/401 |
| GET | `/api/transactions` | Listar por período | 200/400 |
| GET | `/api/transactions/{id}` | Detalhes de uma transação | 200/404 |
| GET | `/health` | Health check | 200/503 |

### Consolidation Service
| Método | Endpoint | Descrição | Status |
|--------|----------|-----------|--------|
| GET | `/api/consolidation/daily` | Saldo consolidado diário | 200/404 |
| GET | `/api/consolidation/daily/{date}` | Saldo de data específica | 200/404 |
| GET | `/health` | Health check | 200/503 |

### API Gateway
| Método | Endpoint | Descrição | Status |
|--------|----------|-----------|--------|
| - | `/api/*` | Roteamento para serviços | Varia |
| POST | `/auth/login` | Autenticação (via Keycloak) | 200/401 |
| POST | `/auth/refresh` | Refresh token | 200/401 |
| GET | `/health` | Saúde geral | 200/503 |

---

## Restrições e Limitações

### Escopo de Funcionalidades
- ❌ Edição de transações (apenas leitura e criação)
- ❌ Deleção de transações (apenas para dados de hoje, não histórico)
- ❌ Múltiplos usuários/comerciantes com isolamento (single-tenant MVP)
- ❌ Relatórios customizáveis (apenas consolidado diário)
- ❌ Notificações/alertas (implementável como feature future)

### Dados Financeiros
- Apenas débito/crédito simples (não há transferências entre contas)
- Sem suporte a múltiplas moedas (apenas BRL)
- Sem suporte a juros compostos ou operações complexas

---

## Critérios de Aceite

- [ ] Todos os casos de uso (UC-01 a UC-04) podem ser testados manualmente
- [ ] Regras de negócio (RN-01 a RN-08) são validadas em testes
- [ ] Endpoints seguem convenções REST
- [ ] Isolamento entre Transactions e Consolidation é garantido por design
- [ ] Documentação é testável (contém exemplos concretos de request/response)
