# 04 — Component Diagram: Consolidation Service + Worker (C4 Level 3)

## Visão Geral

O **Consolidation Service** é composto por **dois containers** com responsabilidades distintas:

- **Consolidation API** — Serviço de **leitura** (consulta de saldo diário consolidado). Usa padrão Cache-First com Redis.
- **Consolidation Worker** — Serviço de **processamento assíncrono**. Consome eventos do RabbitMQ e recalcula saldo.

Os dois compartilham o mesmo banco de dados (`consolidation_db`) mas são deployados de forma independente, o que garante o requisito de isolamento de falhas.

---

## Diagrama — Consolidation API

```mermaid
C4Component
    title Consolidation API — Component Diagram (C4 Level 3)

    Container_Boundary(consapi, "Consolidation API (.NET 8)") {

        Component(endpoints, "ConsolidationEndpoints", "Minimal API", "Define rotas HTTP:\n• GET /api/v1/consolidation/daily\n• GET /api/v1/consolidation/daily/{date}\nMapeia query params → request → response")

        Component(service, "ConsolidationService", "Application Service", "Orquestra leitura com Cache-First:\n1. Busca em cache (Redis)\n2. Se HIT: retorna imediatamente\n3. Se MISS: busca no MongoDB\n4. Armazena em cache (TTL 5min)\n5. Retorna resultado")

        Component(domain, "DailyConsolidation", "Domain Aggregate", "Entidade de consolidação:\n• Date (DateOnly, unique)\n• TotalCredits (decimal)\n• TotalDebits (decimal)\n• Balance (calculado)\n• TransactionCount (int)\n• LastUpdated (DateTime)")

        Component(icache, "IConsolidationCache", "Interface", "Abstração de cache:\n• GetAsync(date)\n• SetAsync(date, data, ttl)\n• InvalidateAsync(date)")

        Component(rediscache, "RedisConsolidationCache", "Infrastructure", "Implementação Redis:\n• Chave: consolidation:{date}\n• TTL: 5 minutos\n• Serialização: JSON\n• Retry em falha de conexão")

        Component(irepo, "IConsolidationRepository", "Interface", "Abstração de persistência:\n• GetByDateAsync(date)\n• UpsertAsync(consolidation)")

        Component(mongorepo, "MongoConsolidationRepository", "Infrastructure", "Implementação MongoDB:\n• Collection: daily_consolidation\n• Índice único em date\n• Upsert (não insert)\n• Decimal128 para valores")
    }

    ContainerDb(redis, "Redis 7.2", "Cache", "Chave: consolidation:{date}\nTTL: 5 minutos")

    ContainerDb(mongodb, "MongoDB", "consolidation_db", "Collection: daily_consolidation")

    Rel(endpoints, service, "Delega consulta para")
    Rel(service, domain, "Mapeia para")
    Rel(service, icache, "Busca/armazena via")
    Rel(service, irepo, "Lê dados via")
    Rel(icache, rediscache, "Implementado por")
    Rel(irepo, mongorepo, "Implementado por")
    Rel(rediscache, redis, "GET/SET/DEL", "Redis protocol")
    Rel(mongorepo, mongodb, "Find/Upsert", "MongoDB driver")
```

---

## Diagrama — Consolidation Worker

```mermaid
C4Component
    title Consolidation Worker — Component Diagram (C4 Level 3)

    Container_Boundary(worker, "Consolidation Worker (.NET 8 BackgroundService)") {

        Component(consumer, "TransactionCreatedConsumer", "RabbitMQ Consumer", "Consome eventos da fila:\n• Queue: consolidation.input\n• At-least-once delivery\n• Chama handler para cada evento\n• ACK após processamento\n• NACK + DLQ após 3 falhas")

        Component(idempotency, "IdempotencyChecker", "Application Service", "Evita processamento duplicado:\n• Busca idempotencyKey no MongoDB\n• Se já processado: ignora silenciosamente\n• Se novo: prossegue + registra")

        Component(calculator, "ConsolidationCalculator", "Domain Service", "Aplica delta incremental ao consolidado:\n• Recebe: type + amount do evento\n• se CREDIT → totalCredits += amount\n• se DEBIT  → totalDebits  += amount\n• Recalcula: balance = totalCredits - totalDebits\n• Retorna DailyConsolidation atualizado")

        Component(irepo, "IConsolidationRepository", "Interface", "Abstração de persistência:\n• UpsertAsync(consolidation, session)\n• GetByDateAsync(date)")

        Component(mongorepo, "MongoConsolidationRepository", "Infrastructure", "Implementação MongoDB:\n• Collection: daily_consolidation\n• UPSERT (nunca duplica por data)\n• Usa IClientSessionHandle")

        Component(iidempotencyrepo, "IIdempotencyRepository", "Interface", "Abstração de idempotência:\n• ExistsAsync(key)\n• RegisterAsync(key, session)")

        Component(mongoidemp, "MongoIdempotencyRepository", "Infrastructure", "Registro de eventos processados:\n• Collection: processed_events\n• TTL index: expira em 7 dias\n• Índice único em idempotencyKey")

        Component(icacheinv, "ICacheInvalidator", "Interface", "Abstração de invalidação:\n• InvalidateAsync(date)")

        Component(redissinv, "RedisCacheInvalidator", "Infrastructure", "Invalida cache do Redis:\n• DEL consolidation:{date}\n• Fire-and-forget (não crítico)\n• Falha silenciosa (cache fica stale)")
    }

    ContainerDb(rabbitmq, "RabbitMQ", "Message Broker", "Queue: consolidation.input\nDLQ: dlx.transaction.created")

    ContainerDb(consmongo, "MongoDB", "consolidation_db", "Collections:\n• daily_consolidation\n• processed_events")

    ContainerDb(redis, "Redis", "Cache", "Chave: consolidation:{date}")

    Rel(consumer, rabbitmq, "Consome de", "AMQP")
    Rel(consumer, idempotency, "Verifica duplicidade via")
    Rel(consumer, irepo, "Lê consolidação atual via")
    Rel(consumer, calculator, "Aplica delta via")
    Rel(consumer, irepo, "Persiste resultado via")
    Rel(consumer, icacheinv, "Invalida cache via")

    Rel(idempotency, iidempotencyrepo, "Consulta/registra via")
    Rel(iidempotencyrepo, mongoidemp, "Implementado por")
    Rel(mongoidemp, consmongo, "Lê/escreve", "MongoDB driver")

    Rel(irepo, mongorepo, "Implementado por")
    Rel(mongorepo, consmongo, "Find/Upsert", "MongoDB driver")

    Rel(icacheinv, redissinv, "Implementado por")
    Rel(redissinv, redis, "DEL key", "Redis protocol")
```

---

## Descrição dos Componentes

### Consolidation API — Componentes

#### ConsolidationEndpoints
**Responsabilidade:** Expor rotas HTTP de consulta de saldo

- Tecnologia: .NET 8 Minimal APIs
- Aceita parâmetro `date` (query param ou path param)
- Valida formato de data e que não é data futura
- Delega ao `ConsolidationService`

**Rotas:**
```
GET  /api/v1/consolidation/daily?date=YYYY-MM-DD    → saldo de data específica
GET  /api/v1/consolidation/daily/{date}             → versão alternativa
GET  /health                                         → health check
GET  /metrics                                        → Prometheus metrics
```

---

#### ConsolidationService
**Responsabilidade:** Orquestrar consulta de saldo com Cache-First

```
FLUXO (Cache-First):
  1. Buscar em Redis: GET consolidation:2024-03-15
  2. HIT → retornar imediatamente (< 50ms)
  3. MISS:
     a. Buscar em MongoDB: daily_consolidation WHERE date = '2024-03-15'
     b. Encontrado → SET Redis com TTL 5min
     c. Não encontrado → 404 Not Found
  4. Retornar DailyConsolidationDto
```

---

#### DailyConsolidation (Domain Aggregate)
**Responsabilidade:** Representar o saldo consolidado de um dia

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `Date` | DateOnly | Data do consolidado (chave única) |
| `TotalCredits` | decimal | Soma de todos os créditos do dia |
| `TotalDebits` | decimal | Soma de todos os débitos do dia |
| `Balance` | decimal | TotalCredits - TotalDebits |
| `TransactionCount` | int | Quantidade de lançamentos processados |
| `LastUpdated` | DateTime | Último recálculo |

**Invariante:** `Balance = TotalCredits - TotalDebits` (calculado, nunca modificado diretamente)

---

#### RedisConsolidationCache
**Responsabilidade:** Cache de alto desempenho para consultas de saldo

- Chave: `consolidation:{date}` (ex: `consolidation:2024-03-15`)
- TTL: 5 minutos (configurável via `Redis__DefaultExpirationMinutes`)
- Serialização: `System.Text.Json`
- Retry: 2 tentativas com delay 100ms antes de falhar silenciosamente
- Fallback: Se Redis está indisponível, consulta vai direto ao MongoDB

---

### Consolidation Worker — Componentes

#### TransactionCreatedConsumer
**Responsabilidade:** Consumir e orquestrar processamento de eventos

**Ciclo completo de um evento:**
```
1. DEQUEUE mensagem de consolidation.input
   (evento carrega: type, amount, date, idempotencyKey)
2. Verificar idempotência (já processou este evento?)
   ├── SIM → ACK e ignorar
   └── NÃO → prosseguir
3. Ler DailyConsolidation atual de consolidation_db para a data do evento
   (ou criar novo registro com zeros se for o primeiro lançamento do dia)
4. Aplicar delta do evento:
   ├── se type = CREDIT → totalCredits += amount
   └── se type = DEBIT  → totalDebits  += amount
   recalcular: balance = totalCredits - totalDebits
               transactionCount += 1
5. BEGIN MongoDB session
   ├── UPSERT daily_consolidation
   └── INSERT processed_events (idempotência)
6. COMMIT session
7. DEL cache Redis: consolidation:{date}
8. ACK mensagem (sucesso)

EM CASO DE FALHA:
   - NACK com requeue=false
   - Após 3 tentativas: mensagem vai para DLQ
   - Alerta para operação manual
```

---

#### IdempotencyChecker
**Responsabilidade:** Garantir que o mesmo evento não seja processado duas vezes

- Verifica `idempotencyKey` (UUID gerado pelo Transactions Service)
- Armazenado em `consolidation_db.processed_events`
- TTL: 7 dias (via índice TTL no MongoDB)
- **Cenário tratado:** RabbitMQ pode entregar a mesma mensagem mais de uma vez por falha de rede ou reinício do consumer

---

#### ConsolidationCalculator
**Responsabilidade:** Aplicar delta incremental ao consolidado do dia

O Calculator **não lê de nenhum banco**. Recebe os dados do evento e o estado atual do consolidado, aplica a lógica de negócio e retorna o documento atualizado.

**Algoritmo:**
```
ENTRADA:
  evento   = { type: CREDIT|DEBIT, amount: decimal, date: DateOnly }
  atual    = DailyConsolidation atual (ou novo com zeros)

APLICAR DELTA:
  se type = CREDIT → atual.totalCredits += amount
  se type = DEBIT  → atual.totalDebits  += amount
  atual.balance          = atual.totalCredits - atual.totalDebits
  atual.transactionCount += 1
  atual.lastUpdated       = DateTime.UtcNow

RETORNAR DailyConsolidation {
  date, totalCredits, totalDebits, balance, transactionCount, lastUpdated
}
```

**Precisão:** Usa `decimal` (nunca `float`/`double`) para evitar erros de arredondamento financeiro.

**Isolamento:** Este componente opera exclusivamente em `consolidation_db`. O evento `TransactionCreated` carrega todos os dados necessários (type + amount + date), conforme definido em ADR-002.

---

#### RedisCacheInvalidator
**Responsabilidade:** Invalidar cache após recálculo

- Operação: `DEL consolidation:{date}`
- **Fire-and-forget:** Falha de invalidação não aborta o processamento
- **Consequência de falha:** Cache fica stale por no máximo 5 minutos (TTL natural)
- Próxima consulta buscará dados frescos do MongoDB

---

## Fluxos de Sequência

### Fluxo 1: Processar TransactionCreated (Worker)

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant CONS as TransactionCreatedConsumer
    participant IDEMP as IdempotencyChecker
    participant REPO as MongoConsolidationRepository
    participant CALC as ConsolidationCalculator
    participant IDMPSTORE as MongoIdempotencyRepository
    participant CACHE as RedisCacheInvalidator

    MQ->>CONS: TransactionCreated { type: CREDIT, amount: 500, date: "2024-03-15", idempotencyKey: "uuid" }

    CONS->>IDEMP: AlreadyProcessed("uuid")?
    IDEMP-->>CONS: ❌ Não processado

    CONS->>REPO: GetByDateAsync("2024-03-15")
    REPO-->>CONS: DailyConsolidation { credits:300, debits:150, balance:150, count:2 }

    CONS->>CALC: ApplyDelta(consolidation, type:CREDIT, amount:500)
    Note over CALC: totalCredits = 300 + 500 = 800<br/>totalDebits  = 150<br/>balance      = 650<br/>count        = 3
    CALC-->>CONS: DailyConsolidation { date, credits:800, debits:150, balance:650, count:3 }

    Note over CONS,IDMPSTORE: MongoDB Transaction (Atomicidade)
    CONS->>REPO: UpsertAsync(consolidation, session)
    REPO-->>CONS: ✅ Upserted

    CONS->>IDMPSTORE: RegisterAsync("uuid", session)
    IDMPSTORE-->>CONS: ✅ Registrado

    Note over CONS: COMMIT session

    CONS->>CACHE: InvalidateAsync("2024-03-15")
    CACHE-->>CONS: ✅ Cache invalidado

    CONS->>MQ: ACK ✅
```

---

### Fluxo 2: Processar Evento Duplicado (Idempotência)

```mermaid
sequenceDiagram
    participant MQ as RabbitMQ
    participant CONS as TransactionCreatedConsumer
    participant IDEMP as IdempotencyChecker

    MQ->>CONS: TransactionCreated { idempotencyKey: "uuid-already-done" }

    CONS->>IDEMP: AlreadyProcessed("uuid-already-done")?
    IDEMP-->>CONS: ✅ Já processado!

    CONS->>MQ: ACK ✅ (ignora silenciosamente)

    Note over CONS: Nenhuma escrita realizada
    Note over CONS: Nenhuma invalidação de cache
    Note over CONS: Idempotência garantida
```

---

### Fluxo 3: Consultar Saldo (Cache HIT)

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as ConsolidationEndpoints
    participant SVC as ConsolidationService
    participant CACHE as RedisConsolidationCache

    Merchant->>GW: GET /api/v1/consolidation/daily?date=2024-03-15 (JWT)
    GW->>GW: Valida JWT + Rate limit
    GW->>EP: Encaminha

    EP->>SVC: GetDailyAsync("2024-03-15")

    SVC->>CACHE: GetAsync("2024-03-15")
    CACHE-->>SVC: ✅ HIT { date, credits, debits, balance, count }

    SVC-->>EP: DailyConsolidationDto
    EP-->>GW: 200 OK
    GW-->>Merchant: 200 OK { date, totalCredits, totalDebits, balance }

    Note over SVC: Duração: < 50ms
```

---

### Fluxo 4: Consultar Saldo (Cache MISS)

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as ConsolidationEndpoints
    participant SVC as ConsolidationService
    participant CACHE as RedisConsolidationCache
    participant REPO as MongoConsolidationRepository

    Merchant->>GW: GET /api/v1/consolidation/daily?date=2024-03-15 (JWT)
    GW->>GW: Valida JWT + Rate limit
    GW->>EP: Encaminha

    EP->>SVC: GetDailyAsync("2024-03-15")

    SVC->>CACHE: GetAsync("2024-03-15")
    CACHE-->>SVC: ❌ MISS (expirou ou nunca armazenado)

    SVC->>REPO: GetByDateAsync("2024-03-15")
    REPO-->>SVC: DailyConsolidation { ... }

    SVC->>CACHE: SetAsync("2024-03-15", data, ttl=5min)
    CACHE-->>SVC: ✅ Armazenado

    SVC-->>EP: DailyConsolidationDto
    EP-->>GW: 200 OK
    GW-->>Merchant: 200 OK { date, totalCredits, totalDebits, balance }

    Note over SVC: Duração: 200-500ms
```

---

### Fluxo 5: Consultar Saldo (Data Não Encontrada)

```mermaid
sequenceDiagram
    actor Merchant as Comerciante
    participant GW as API Gateway
    participant EP as ConsolidationEndpoints
    participant SVC as ConsolidationService
    participant CACHE as RedisConsolidationCache
    participant REPO as MongoConsolidationRepository

    Merchant->>GW: GET /api/v1/consolidation/daily?date=2000-01-01 (JWT)
    GW->>EP: Encaminha

    EP->>SVC: GetDailyAsync("2000-01-01")

    SVC->>CACHE: GetAsync("2000-01-01")
    CACHE-->>SVC: ❌ MISS

    SVC->>REPO: GetByDateAsync("2000-01-01")
    REPO-->>SVC: ❌ null (não existe)

    SVC-->>EP: null
    EP-->>GW: 404 Not Found
    GW-->>Merchant: 404 Not Found { "message": "No consolidation found for 2000-01-01" }
```

---

## Isolamento e Resiliência

### Worker DOWN → API continua operando

```
Cenário: Worker está down por 1 hora

Durante o downtime:
  ✅ Consolidation API: Retorna dados do cache/DB (possivelmente defasado)
  ✅ Transactions API: Continua registrando lançamentos normalmente
  ✅ RabbitMQ: Acumula mensagens na fila (consolidation.input)

Quando Worker volta:
  1. Consome todas as mensagens acumuladas (ordem preservada)
  2. IdempotencyChecker garante que duplicatas são ignoradas
  3. Consolidado atualizado em segundos
  4. Cache invalidado automaticamente
```

### Redis DOWN → API degrada graciosamente

```
Cenário: Redis está down

  ✅ ConsolidationService: Detecta falha no cache → vai direto para MongoDB
  ✅ API responde normalmente (mas mais lenta: 200-500ms vs < 50ms)
  ⚠️ Worker: CacheInvalidator falha silenciosamente (fire-and-forget)
  ✅ Quando Redis volta: cache se popula naturalmente na próxima consulta
```

---

## Padrões Aplicados

| Padrão | Onde Aplicado | Benefício |
|--------|--------------|-----------|
| **Cache-First** | ConsolidationService | Latência < 50ms no happy path |
| **At-Least-Once + Idempotência** | Worker + IdempotencyChecker | Segurança de reprocessamento |
| **Upsert** | MongoConsolidationRepository | Uma data = um documento (RN-04) |
| **Fire-and-forget** | RedisCacheInvalidator | Falha de cache não aborta processamento |
| **Event-Carried State Transfer** | TransactionCreatedConsumer | Worker usa dados do evento — sem cross-DB read |
| **Delta Incremental** | ConsolidationCalculator | Atualização eficiente sem recálculo completo |

---

**Próximo documento:** `docs/architecture/06-architectural-patterns.md` (padrões adotados com justificativas)
