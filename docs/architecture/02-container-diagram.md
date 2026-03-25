# 02 — Container Diagram (C4 Level 2)

## Visão Geral

O **Container Diagram** detalha a estrutura interna do **CashFlow System**. Mostra:
- **4 containers de aplicação** (.NET 8 APIs e Worker)
- **3 data stores** (MongoDB, Redis, RabbitMQ)
- **1 Identity Provider** (Keycloak)
- **Stack de observabilidade** (OTel, Jaeger, Prometheus, Grafana, Seq)
- **3 limites de rede** (frontend, backend, monitoring)
- **Fluxos de dados** (síncrono e assíncrono)

Um "container" aqui é um processo executável ou microserviço — algo que pode ser deployado independentemente.

---

## Diagrama

```mermaid
C4Container
    title CashFlow System — Container Diagram
    
    Person(merchant, "Comerciante", "Acessa via navegador ou cliente REST")
    
    System_Boundary("frontend-net", "Frontend Network") {
        Container(gateway, "API Gateway", "YARP (.NET 8)", "• Ponto de entrada único<br/>• Rate limiting (100 req/s)<br/>• Auth middleware (JWT validation)<br/>• Roteamento para downstream services")
    }
    
    System_Boundary("backend-net", "Backend Network") {
        Container(transapi, "Transactions API", ".NET 8 Minimal API", "• POST /transactions (criar lançamento)<br/>• GET /transactions (listar por período)<br/>• MongoDB persistência<br/>• Outbox Pattern + RabbitMQ publish")
        
        Container(consapi, "Consolidation API", ".NET 8 Minimal API", "• GET /consolidation/daily (saldo consolidado)<br/>• Cache-first (Redis)<br/>• Fallback para MongoDB<br/>• TTL 5 minutos")
        
        Container(worker, "Consolidation Worker", ".NET 8 BackgroundService", "• RabbitMQ consumer<br/>• Processa TransactionCreated events<br/>• Recalcula saldo diário<br/>• Invalida cache Redis<br/>• Idempotência garantida")
        
        ContainerDb(mongodb, "MongoDB 7.0", "Database", "• transactions_db<br/>  └─ transactions collection<br/>• consolidation_db<br/>  └─ daily_consolidation collection<br/>• Segurança: replicaset ready")
        
        ContainerDb(redis, "Redis 7.2", "Cache", "• Cache de consolidado (TTL 5min)<br/>• Chave: consolidation:{date}<br/>• LRU eviction policy")
        
        ContainerDb(rabbitmq, "RabbitMQ 3.13", "Message Broker", "• Exchange: events<br/>• Queue: transaction.created<br/>• Queue: consolidation.input<br/>• DLQ para retry fallidos<br/>• Prometheus metrics enabled")
    }
    
    System_Boundary("keycloak-net", "Identity Provider Network") {
        Container(keycloak, "Keycloak 24.0", "Identity Provider", "• OAuth 2.0 + OpenID Connect<br/>• RBAC: transactions:read/write, consolidation:read<br/>• JWT tokens (1h expiry)<br/>• Refresh tokens (7d expiry)")
    }
    
    System_Boundary("monitoring-net", "Monitoring Network") {
        Container(otel, "OTel Collector", "Observability", "• Coleta traces (OTLP gRPC)<br/>• Coleta logs estruturados<br/>• Coleta métricas<br/>• Exporta para Jaeger, Seq, Prometheus")
        
        Container(jaeger, "Jaeger UI", "Distributed Tracing", "• Visualiza traces distribuídos<br/>• Armazena spans em badger<br/>• Análise de latência")
        
        Container(prometheus, "Prometheus", "Time-Series Metrics", "• Scrape de métricas (9090)<br/>• Retenção: 7 dias<br/>• Targets: APIs, RabbitMQ, Redis")
        
        Container(grafana, "Grafana", "Visualization", "• Dashboards de métricas<br/>• Fonte de dados: Prometheus<br/>• Alertas configurados")
        
        Container(seq, "Seq", "Structured Logging", "• Coleta logs estruturados em JSON<br/>• Busca full-text<br/>• Alertas baseados em padrões")
    }
    
    System_Ext(browser, "Browser / REST Client", "Aplicação do comerciante")
    
    Rel(merchant, browser, "Acessa via")
    
    Rel(browser, gateway, "HTTP/HTTPS", "REST APIs")
    
    Rel(gateway, transapi, "Rota para /transactions", "HTTP 8080")
    Rel(gateway, consapi, "Rota para /consolidation", "HTTP 8080")
    Rel(gateway, keycloak, "Valida token com", "OAuth 2.0")
    
    Rel(transapi, mongodb, "Persiste lançamentos", "MongoDB driver")
    Rel(transapi, rabbitmq, "Publica TransactionCreated", "AMQP")
    Rel(transapi, otel, "Envia spans + métricas", "OTLP gRPC")
    
    Rel(consapi, redis, "Busca cache (hit)", "Redis protocol")
    Rel(consapi, mongodb, "Busca DB (miss)", "MongoDB driver")
    Rel(consapi, otel, "Envia spans + métricas", "OTLP gRPC")
    
    Rel(worker, rabbitmq, "Consome eventos", "AMQP")
    Rel(worker, mongodb, "Lê transações + escreve consolidado", "MongoDB driver")
    Rel(worker, redis, "Invalida cache", "Redis protocol")
    Rel(worker, otel, "Envia spans + métricas", "OTLP gRPC")
    
    Rel(otel, jaeger, "Exporta traces")
    Rel(otel, seq, "Exporta logs")
    Rel(otel, prometheus, "Exporta métricas")
    
    Rel(prometheus, grafana, "Scrape de métricas")
```

---

## Descrição Detalhada dos Containers

### Containers de Aplicação

#### 1. API Gateway (YARP)
**Responsabilidade:** Ponto de entrada único, roteamento, segurança

- **Tecnologia:** YARP (Yet Another Reverse Proxy) em .NET 8
- **Porta:** 8080 (exposto em 8080:8080)
- **Funcionalidades:**
  - Rate limiting: 100 req/s (global por IP)
  - Validação de JWT (Authorization header)
  - Roteamento para serviços downstream
  - Logs de requisição
  - Tracing OpenTelemetry
- **Dependências:**
  - Keycloak (validação de tokens)
  - Transactions API (downstream)
  - Consolidation API (downstream)

**SLA:** 99.99% uptime (4s downtime/mês)

---

#### 2. Transactions API
**Responsabilidade:** Ingestão de lançamentos (débitos/créditos)

- **Tecnologia:** .NET 8 Minimal APIs
- **Porta:** 8080 (exposto em 8081:8080 no docker-compose)
- **Endpoints:**
  - `POST /api/v1/transactions` — Criar lançamento
  - `GET /api/v1/transactions` — Listar por período (paginado)
  - `GET /api/v1/transactions/{id}` — Detalhes de uma transação
- **Padrão:** Outbox Pattern
  - Insert em `transactions` collection
  - Insert em `outbox` collection (para garantir publicação)
  - Ambos em mesma transação MongoDB
  - Publicação para RabbitMQ após commit
- **Cache:** Não usa (dados mutáveis)
- **Dependências:**
  - MongoDB (transactions_db)
  - RabbitMQ (publicar eventos)
  - OTel Collector (tracing/métricas)

**SLA:** 99.9% uptime (43s downtime/mês)

**Latência:**
- p50: ≤ 200ms
- p95: ≤ 1000ms
- p99: ≤ 2000ms

**Throughput:** ≥ 100 req/s

---

#### 3. Consolidation API
**Responsabilidade:** Leitura de saldo consolidado diário

- **Tecnologia:** .NET 8 Minimal APIs
- **Porta:** 8080 (exposto em 8082:8080 no docker-compose)
- **Endpoints:**
  - `GET /api/v1/consolidation/daily?date=YYYY-MM-DD` — Saldo de data específica
  - `GET /api/v1/consolidation/daily/{date}` — Versão alternativa
- **Padrão:** Cache-First
  1. Busca em Redis (TTL 5min)
  2. Se HIT: retorna imediatamente (< 50ms)
  3. Se MISS: busca em MongoDB, armazena em Redis, retorna
- **Dependências:**
  - Redis (cache)
  - MongoDB (consolidation_db)
  - OTel Collector (tracing/métricas)

**SLA:** 99.5% uptime (3.6min downtime/mês)

**Latência:**
- Cache HIT: < 50ms
- Cache MISS: 200-500ms
- p95: ≤ 500ms
- p99: ≤ 1500ms

**Throughput:** ≥ 50 req/s (crítico)

---

#### 4. Consolidation Worker
**Responsabilidade:** Processamento assíncrono de consolidações

- **Tecnologia:** .NET 8 BackgroundService (não é HTTP)
- **Trigger:** Consome mensagens `TransactionCreated` de RabbitMQ
- **Processo:**
  1. Consome evento `TransactionCreated` da fila
  2. Verifica idempotência (já foi processado?)
  3. Busca todas as transações da data
  4. Calcula: `balance = sum(credits) - sum(debits)`
  5. UPSERT em `consolidation_db.daily_consolidation`
  6. DELETE chave de cache em Redis: `consolidation:{date}`
  7. ACK mensagem ao RabbitMQ
- **Retry:** Exponential backoff (1s, 2s, 4s)
- **DLQ:** Mensagens com 3+ falhas vão para dead-letter queue para investigação manual
- **Dependências:**
  - RabbitMQ (consumer)
  - MongoDB (consolidation_db)
  - Redis (cache invalidation)
  - OTel Collector (tracing/métricas)

**Resiliência:**
- Se RabbitMQ falha: mensagens ficam enfileiradas
- Se MongoDB falha: retry com backoff exponencial
- Se Redis falha: continua sem cache (próxima leitura buscará do DB)

---

### Data Stores

#### MongoDB 7.0
**Responsabilidade:** Persistência de dados estruturados

- **Imagem:** mongo:7.0
- **Autenticação:** MONGO_INITDB_ROOT_USERNAME/PASSWORD
- **Limite de memória:** 512M
- **Databases:**
  - `transactions_db` — Dados de lançamentos
    - Collection: `transactions`
      - `_id` (ObjectId)
      - `userId` (String — extraído do JWT, identifica o autor do lançamento)
      - `type` (DEBIT|CREDIT)
      - `amount` (Decimal128)
      - `description` (String)
      - `category` (String)
      - `date` (Date)
      - `createdAt` (Timestamp)
      - `updatedAt` (Timestamp)
      - Índice: `date` + `type`
      - Índice: `userId` (para consultas de auditoria por usuário)
    - Collection: `outbox` (Outbox Pattern)
      - Armazena eventos aguardando publicação
  - `consolidation_db` — Dados de consolidação
    - Collection: `daily_consolidation`
      - `date` (Date, unique)
      - `totalCredits` (Decimal128)
      - `totalDebits` (Decimal128)
      - `balance` (Decimal128, calculado)
      - `transactionCount` (Int32)
      - `lastUpdated` (Timestamp)
      - Índice: `date` (unique)
- **Backup:** Volumes nomeados (persistem além de `docker compose down`)

---

#### Redis 7.2
**Responsabilidade:** Cache de leitura rápida

- **Imagem:** redis:7.2-alpine
- **Configuração:**
  - `requirepass` (autenticação)
  - `maxmemory` 128M
  - `maxmemory-policy` allkeys-lru (evict oldest keys se cheio)
  - `appendonly yes` (AOF persistence)
- **Estrutura de chaves:**
  - `consolidation:{date}` → JSON da consolidação (TTL 5min)
  - Formato chave: `consolidation:2024-03-15`
- **Política de limpeza:**
  - Se chegar a 128M, evicta as chaves menos recentemente usadas (LRU)
  - TTL 5 minutos automático (Redis evita dados stale)

---

#### RabbitMQ 3.13
**Responsabilidade:** Broker de eventos assíncrono

- **Imagem:** rabbitmq:3.13-management-alpine
- **Management UI:** Port 15672
- **Prometheus metrics:** Port 15692
- **Configuração:**
  - Single-node (dev/test)
  - Autenticação: RABBITMQ_DEFAULT_USER/PASS
  - Limite de memória: 512M
- **Topologia:**
  - **Exchange:** `events` (type: topic)
  - **Queues:**
    - `transaction.created` — Events do Transactions Service
    - `consolidation.input` — Input para o Worker
    - Dead Letter Exchanges/Queues para retry
  - **Bindings:**
    - `transaction.created` → `consolidation.input` (Consolidation Worker consome)
- **Padrão:** At-least-once delivery com idempotência no consumer

---

### Identity Provider

#### Keycloak 24.0
**Responsabilidade:** Autenticação e autorização centralizadas

- **Imagem:** quay.io/keycloak/keycloak:24.0
- **Banco de dados:** PostgreSQL 16 (separado)
- **Porta:** 8080 (exposto em 8443:8080 no docker-compose — NOTA: porta interna é 8080, não HTTPS)
- **Configuração:**
  - Realm: `cashflow`
  - Usuarios: Admin (KEYCLOAK_ADMIN/KEYCLOAK_ADMIN_PASSWORD)
  - Clients: cashflow-api
- **Suporte:**
  - OAuth 2.0 Authorization Code Flow
  - OpenID Connect
  - RBAC (Roles): transactions:read, transactions:write, consolidation:read, admin
- **Tokens:**
  - JWT access token: 1 hora de validade
  - Refresh token: 7 dias de validade

---

### Stack de Observabilidade

#### OTel Collector
**Responsabilidade:** Coleta centralizada de sinais observabilidade

- **Imagem:** otel/opentelemetry-collector-contrib:0.98.0
- **Portas:**
  - 4317: OTLP gRPC (input das aplicações)
  - 4318: OTLP HTTP (alternativa)
  - 8889: Prometheus exporter (para Prometheus scrape)
- **Fluxo:**
  - Aplicações enviam traces via OTLP gRPC
  - OTel Collector processa e enriquece spans
  - Exporta para Jaeger (traces), Seq (logs), Prometheus (métricas)

#### Jaeger UI
**Responsabilidade:** Visualização de traces distribuídos

- **Imagem:** jaegertracing/all-in-one:1.56
- **Porta:** 16686 (UI)
- **Armazenamento:** Badger (embedded key-value store)
- **Uso:**
  - Buscar traces por traceId, serviço, operação
  - Analisar latência entre spans
  - Identificar gargalos em fluxos assíncrono

#### Prometheus
**Responsabilidade:** Coleta e armazenamento de métricas

- **Imagem:** prom/prometheus:v2.51.0
- **Porta:** 9090
- **Configuração:**
  - Retention: 7 dias
  - Scrape interval: 15s
  - Targets:
    - APIs (.NET 8 /metrics)
    - RabbitMQ (15692)
    - Redis (exporters)
    - OTel Collector (8889)

#### Grafana
**Responsabilidade:** Visualização de dashboards

- **Imagem:** grafana/grafana:10.4.2
- **Porta:** 3000
- **Fonte de dados:** Prometheus
- **Dashboards:** Provisioning automático a partir de `/etc/grafana/provisioning`

#### Seq
**Responsabilidade:** Armazenamento e busca de logs estruturados

- **Imagem:** datalust/seq:2024.2
- **Porta:** 8341 (UI)
- **Entrada:** OTLP via OTel Collector
- **Formato:** JSON estruturado
- **Uso:**
  - Busca full-text de logs
  - Alertas baseados em padrões

---

## Fluxos de Dados

### Fluxo 1: Criar Lançamento (Happy Path)

```
1. Comerciante
   └─ POST /api/v1/transactions (com JWT)

2. API Gateway
   ├─ Rate limit: verifica limite (100 req/s)
   ├─ Auth: valida JWT com Keycloak
   └─ Rota: encaminha para Transactions API

3. Transactions API
   ├─ Valida input (amount > 0, description não vazio, etc.)
   ├─ BEGIN TRANSACTION MongoDB
   ├─ INSERT em transactions_db.transactions
   ├─ INSERT em transactions_db.outbox (evento TransactionCreated)
   ├─ COMMIT TRANSACTION
   └─ RETURN 201 Created

4. Outbox Publisher (background)
   ├─ Lê evento do outbox
   ├─ PUBLISH em RabbitMQ (exchange: events, routing_key: transaction.created)
   └─ DELETE do outbox

5. RabbitMQ
   └─ Armazena mensagem na fila consolidation.input

6. Consolidation Worker (consome)
   ├─ Consome mensagem
   ├─ Idempotency check (já foi processado?)
   ├─ Busca todas as transações da data em transactions_db
   ├─ Calcula balance = sum(credits) - sum(debits)
   ├─ UPSERT em consolidation_db.daily_consolidation
   ├─ DELETE cache em Redis: consolidation:2024-03-15
   ├─ ACK mensagem
   └─ Consolidado atualizado!

Duração típica: T0 a T0+500ms (inclusve processamento assíncrono)
```

---

### Fluxo 2: Consultar Saldo Consolidado (Cache Hit)

```
1. Comerciante
   └─ GET /api/v1/consolidation/daily?date=2024-03-15 (com JWT)

2. API Gateway
   ├─ Rate limit: verifica (100 req/s)
   ├─ Auth: valida JWT
   └─ Rota: encaminha para Consolidation API

3. Consolidation API
   ├─ Busca em Redis: consolidation:2024-03-15
   ├─ HIT! Cache encontrado
   ├─ Retorna resultado
   └─ RETURN 200 OK

Duração: < 50ms (operação local em memória)
```

---

### Fluxo 3: Consultar Saldo Consolidado (Cache Miss)

```
1-2. (Igual ao fluxo anterior até Consolidation API)

3. Consolidation API
   ├─ Busca em Redis: consolidation:2024-03-15
   ├─ MISS! Cache expirou ou não existe
   ├─ Busca em MongoDB: consolidation_db.daily_consolidation
   ├─ Encontrou resultado
   ├─ STORE em Redis (TTL 5 min): consolidation:2024-03-15
   ├─ Retorna resultado
   └─ RETURN 200 OK

Duração: 200-500ms (query MongoDB + serialização)
```

---

## Limites de Rede

### Frontend Network (`frontend-net`)
- **Quem está:** API Gateway
- **Acesso:** Comerciante via HTTPS (porta 8080)
- **Propósito:** Isolamento: apenas ponto de entrada público

### Backend Network (`backend-net`)
- **Quem está:** Transactions API, Consolidation API, Worker, MongoDB, Redis, RabbitMQ, OTel Collector
- **Acesso:** Apenas entre serviços internos
- **Segurança:** Sem acesso direto de fora

### Monitoring Network (`monitoring-net`)
- **Quem está:** Jaeger, Prometheus, Grafana, Seq, OTel Collector
- **Acesso:** Acesso externo apenas para dashboards (Grafana:3000, Jaeger:16686)
- **Propósito:** Observabilidade isolada

---

## Isolamento de Falhas

| Cenário | Transactions | Consolidation | Resultado |
|---------|--------------|---------------|-----------|
| Worker DOWN | ✅ 100% funcional | ⚠️ Desatualizado | Novos lançamentos criados, consolidado fica com dados velhos |
| Redis DOWN | ✅ Funcional | ⚠️ Sem cache | Consolidation API mais lenta (queries do MongoDB) |
| MongoDB DOWN | ❌ Falha | ❌ Falha | Ambos serviços indisponíveis |
| RabbitMQ DOWN | ⚠️ Degradado | ⚠️ Degradado | Lançamentos criados mas não propagam para consolidado |

**Padrão:** Circuit breaker + bulkhead + timeout para resiliência adicional

---

## Próximos Níveis

- **C4 Level 3 (Component Diagrams):** Mostrarão a estrutura interna de cada serviço
  - Transactions API: Controllers, Services, Repositories, Domain Models
  - Consolidation API: Controllers, Services, Cache Layer, Repositories
  - Worker: Consumer pattern, Domain Services, Repositories
  
- **Fluxos de Sequência:** Diagramas de interação passo-a-passo para cenários críticos

---

**Próximo documento:** `docs/architecture/03-component-transactions.md` (C4 Level 3 — Transactions Service)
