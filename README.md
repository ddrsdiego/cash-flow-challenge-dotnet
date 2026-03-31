# 💰 CashFlow System — Desafio Técnico Arquiteto de Soluções

Sistema de controle de fluxo de caixa para comerciantes, composto por dois serviços desacoplados:
- **Transactions API** — Registro de lançamentos (débitos e créditos)
- **Consolidation Service** — Relatório diário de saldo consolidado

---

## 📋 Índice

- [Visão Geral](#-visão-geral)
- [Arquitetura](#-arquitetura)
- [Estrutura do Repositório](#-estrutura-do-repositório)
- [Stack Tecnológica](#-stack-tecnológica)
- [Requisitos Não Funcionais](#-requisitos-não-funcionais)
- [Pré-requisitos](#-pré-requisitos)
- [Execução Local](#-execução-local)
- [Documentação](#-documentação)
- [Status de Implementação](#-status-de-implementação)

---

## 🎯 Visão Geral

Um comerciante precisa controlar seu fluxo de caixa diário com lançamentos de débitos e créditos, e consultar um relatório com o saldo diário consolidado.

### Requisitos Críticos

| Requisito | Como Atendemos |
|-----------|----------------|
| Lançamentos **NÃO** ficam indisponíveis se Consolidado falhar | Comunicação **assíncrona via RabbitMQ** — serviços completamente desacoplados |
| Consolidado suporta **50 req/s com ≤ 5% de perda** | **Cache-First com IMemoryCache** (TTL 5min in-process) + horizontal scaling + MongoDB para persistência |

### Fluxo Principal

```
Comerciante
    │
    │ Requisição com JWT Bearer Token
    ▼
┌──────────────────────────────────────────┐
│            API Gateway (YARP)            │  ← Porta :8080
│  • Valida JWT via Keycloak               │
│  • Rate Limiting (Fixed Window per IP)   │
│  • Roteamento para downstream services   │
└──────────────┬─────────────────────────────┘
               │
               ▼
        ┌─────────────────────────┐
        │  Transactions API       │  ← Porta :8081
        │ (RawRequest + Outbox)   │
        └─────────────┬───────────┘
                      │
               ┌──────┴──────┐
               │             │
               ▼             ▼
        ┌─────────────┐   ┌──────────┐
        │ Transactions│   │ RabbitMQ │
        │  .Worker    │   │(Event    │
        │ (Batch Proc)    │Broker)   │
        └──────┬──────┘   └─────┬────┘
               │                │
        ┌──────┘                │
        │                       │
        ▼                       ▼
    ┌────────────────────────────────┐
    │ Consolidation.Worker           │
    │ (Batch Ingestion + Processing) │
    └────────────────┬───────────────┘
                     │
                     ▼
    ┌──────────────────────────────┐
    │  Consolidation API           │  ← Porta :8082
    │ (Cache-First + IMemoryCache) │
    └────────────────┬─────────────┘
                     │
        ┌────────────┴────────────┐
        │ (HIT < 50ms)            │ (MISS 200-500ms)
        ▼                         ▼
   ┌──────────┐         ┌──────────────────┐
   │IMemory   │         │   MongoDB        │
   │Cache     │         │consolidation_db  │
   │(TTL 5min)│         └──────────────────┘
   └──────────┘
```

---

## 🔐 Autenticação

### Keycloak — Realm `cashflow`

| Parâmetro | Valor (Docker) | Valor (Local) |
|-----------|---------------|---------------|
| **Authority** | `http://keycloak:8080/realms/cashflow` | `http://localhost:8080/realms/cashflow` |
| **Client ID** | `cashflow-api` | `cashflow-api` |
| **Audience** | `cashflow-api` | `cashflow-api` |

### Usuários de Demonstração

| Usuário | Senha | Role |
|---------|-------|------|
| `merchant-demo` | `demo123` | `merchant` |
| `admin-demo` | `admin123` | `admin` |

### Obter Token JWT

```bash
curl -X POST http://localhost:8080/realms/cashflow/protocol/openid-connect/token \
  -d "client_id=cashflow-api" \
  -d "client_secret=cashflow-api-secret-change-in-production" \
  -d "username=merchant-demo" \
  -d "password=demo123" \
  -d "grant_type=password"
```

### Fluxo de Autenticação

```
Cliente → Bearer Token → Gateway (valida JWT)
                              │ Encaminha Authorization header
                              ▼
                     Transactions API (revalida JWT — defense in depth)
                     Consolidation API (revalida JWT — defense in depth)
```

### Configuração por Serviço

| Serviço | Authentication | Authorization | Rate Limiting |
|---------|---------------|---------------|---------------|
| **Gateway** | JWT Bearer | Policy `"default"` nas rotas YARP | Fixed Window 100 req/s por IP |
| **Transactions API** | JWT Bearer | `RequireAuthorization()` | — (delegado ao Gateway) |
| **Consolidation API** | JWT Bearer | `RequireAuthorization()` | — (delegado ao Gateway) |

---

## 🏗️ Arquitetura

### Padrões Adotados

| Padrão | Onde | Benefício |
|--------|------|-----------|
| **MassTransit + MongoDB Outbox** | Transactions API | Atomicidade entre persistência e mensageria |
| **Cache-First (IMemoryCache)** | Consolidation API | Latência < 10ms (HIT), suporte a 50+ req/s em single-instance |
| **Event-Driven** | Transactions → Worker | Isolamento total de falhas |
| **CQRS Light** | Consolidation Service | Separação de responsabilidades read/write |
| **Database-per-Service** | MongoDB | Isolamento de dados entre serviços |
| **API Gateway (YARP)** | Gateway | Ponto de entrada único, segurança centralizada |

### Decisões Arquiteturais (ADRs)

| ADR | Decisão |
|-----|---------|
| [ADR-001](docs/decisions/ADR-001-async-communication.md) | Comunicação assíncrona via RabbitMQ |
| [ADR-002](docs/decisions/ADR-002-database-per-service.md) | Database per Service com MongoDB |
| [ADR-003](docs/decisions/ADR-003-user-context-propagation.md) | Propagação de contexto de usuário via JWT |
| [ADR-004](docs/decisions/ADR-004-api-gateway.md) | API Gateway com YARP |
| [ADR-005](docs/decisions/ADR-005-authentication-strategy.md) | Autenticação com Keycloak + OAuth2/JWT |
| [ADR-006](docs/decisions/ADR-006-observability-stack.md) | Observabilidade com OpenTelemetry |

---

## 📁 Estrutura do Repositório

```
cash-flow-challenge-dotnet/
├── CashFlow.sln
├── Directory.Build.props          # Configurações globais de build
├── Directory.Packages.props       # Versões centralizadas de NuGet
├── global.json                    # Versão do .NET SDK
├── docker-compose.yml             # Infraestrutura local (MongoDB, Redis, RabbitMQ, Keycloak)
│
├── src/
│   ├── CashFlow.SharedKernel/           # Contratos compartilhados ✅
│   │   ├── Domain/
│   │   │   ├── Entities/                # Transaction, DailyConsolidation, RawRequest, DistributedLock
│   │   │   └── Enums/                   # TransactionType, Category, RawRequestStatus
│   │   ├── Messages/                    # TransactionCreatedEvent, TransactionBatchReadyEvent
│   │   ├── DTOs/
│   │   │   ├── Requests/                # CreateTransactionRequest
│   │   │   └── Responses/               # TransactionResponse, DailyConsolidationResponse, PagedResult
│   │   └── Interfaces/                  # ITransactionRepository, IRawRequestRepository,
│   │                                    # IDistributedLockRepository, ITransactionalPublisher
│   │
│   ├── CashFlow.Transactions.API/       # API de Lançamentos (Fast Ingestion) ✅
│   │   ├── Endpoints/                   # POST /transactions (202 Accepted)
│   │   ├── UseCases/CreateTransaction   # Command + Handler (3 fases)
│   │   └── Infrastructure/MongoDB       # RawRequestRepository
│   │
│   ├── CashFlow.Transactions.Worker/    # Worker de Processamento (Batch) ✅
│   │   ├── Workers/BatcherBackgroundService    # Polling + Distributed Lock
│   │   ├── Consumers/TransactionBatchReady     # MassTransit Consumer
│   │   ├── UseCases/                           # DispatchTransactionBatch + ProcessTransactionBatch
│   │   └── Infrastructure/MongoDB              # RawRequestRepository, TransactionRepository, DistributedLockRepository
│   │
│   ├── CashFlow.Consolidation.Worker/  # Worker de Consolidação 🔄
│   ├── CashFlow.Consolidation.API/     # API de Consolidado ✅
│   └── CashFlow.Gateway/               # API Gateway (YARP) ✅
│
├── tests/
│   ├── CashFlow.Transactions.Tests/
│   ├── CashFlow.Consolidation.Tests/
│   └── CashFlow.Integration.Tests/
│
├── infra/
│   └── config/                    # Configurações de infraestrutura (Keycloak, MongoDB, OTel)
│
└── docs/
    ├── architecture/              # Diagramas C4 (L1, L2, L3)
    ├── security/                  # Arquitetura de segurança
    ├── decisions/                 # ADRs
    ├── operations/                # Deploy, Observabilidade, Scaling, DR
    └── requirements/              # Requisitos funcionais e não funcionais
```

> **Legenda:** ✅ Implementado | 🔄 Em andamento | 📋 Planejado

---

## 🛠️ Stack Tecnológica

| Componente | Tecnologia | Versão | Justificativa |
|-----------|-----------|--------|---------------|
| **APIs** | .NET 8 Minimal APIs | 8.0 | Performance, AOT-ready |
| **Gateway** | YARP (Yet Another Reverse Proxy) | 2.1.0 | Nativo .NET, extensível em C# |
| **Message Broker** | RabbitMQ | 3.x | Confiável, DLQ, at-least-once delivery |
| **Mensageria** | MassTransit + MongoDB Outbox | 8.2.5 | Atomicidade transacional, sem reinventar outbox |
| **DB Lançamentos** | MongoDB 7.0 | 2.28.0 (driver) | NoSQL flexível, suporte a transações |
| **DB Consolidado** | MongoDB 7.0 | 2.28.0 (driver) | Database-per-service isolation |
| **Cache** | IMemoryCache (.NET) | 8.0 | Cache in-process, TTL 5min, < 50ms; Redis disponível para evolução futura |
| **Auth** | Keycloak | 24.x | OAuth2/OIDC, RBAC, open-source |
| **Observabilidade** | OpenTelemetry + Seq + Jaeger + Prometheus | 1.8.x | Vendor-neutral, sem lock-in |
| **Functional Types** | CSharpFunctionalExtensions | 3.6.0 | Maybe<T>, Result<T> idiomáticos |

---

## 📊 Requisitos Não Funcionais

### RNF-01: Isolamento de Falhas
> O serviço de controle de lançamentos **NÃO** deve ficar indisponível caso o serviço de consolidado diário falhe.

**Como atendemos:** Comunicação exclusivamente assíncrona via RabbitMQ. Transactions API publica evento e retorna 201 imediatamente, sem depender do Consolidation Service.

### RNF-02: Throughput
> Em picos, o serviço de consolidado recebe **50 req/s com no máximo 5% de perda**.

**Como atendemos:**
- Cache-First com IMemoryCache (TTL 5min) → p95 < 50ms no happy path
- Cache miss → MongoDB (200-500ms)
- Horizontal scaling via réplicas stateless (cada pod com seu cache local)
- Rate limiting no API Gateway (100 req/s por IP)

### RNF-03: Consistência de Cache em Múltiplas Réplicas
> Com N replicas da Consolidation API, todos os pods devem receber a invalidação de cache **simultaneamente**.

**Como atendemos:**
- Padrão **Fanout per-Instance** com RabbitMQ
- Cada pod cria sua própria fila (`consolidation.api.cache-{unique-id}`) vinculada à exchange fanout
- Fila é auto-deletada quando o pod para (`AutoDelete = true`)
- Resultado: **todos os pods recebem a mensagem de invalidação simultaneamente**, não há competing consumers
- Ver: [Fanout per-Instance Pattern](docs/architecture/04-component-consolidation.md#padrão-fanout-per-instance-para-múltiplas-réplicas)

---

## ⚙️ Pré-requisitos

- **Docker** e **Docker Compose**
- **.NET 8 SDK** (`dotnet --version` deve retornar `8.x`)

---

## 🚀 Execução Local

### 1. Subir a Infraestrutura

```bash
docker-compose up -d
```

Isso iniciará:
- MongoDB (porta 27017)
- Redis (porta 6379)
- RabbitMQ (porta 5672, management UI: 15672)
- Keycloak (porta 8080)
- OpenTelemetry Collector
- Jaeger (porta 16686)
- Prometheus (porta 9090)
- Grafana (porta 3000)

### 2. Executar os Serviços (quando implementados)

```bash
# Transactions API
cd src/CashFlow.Transactions.API && dotnet run

# Consolidation Worker
cd src/CashFlow.Consolidation.Worker && dotnet run

# Consolidation API
cd src/CashFlow.Consolidation.API && dotnet run

# Gateway
cd src/CashFlow.Gateway && dotnet run
```

### 3. Executar Testes

```bash
dotnet test CashFlow.sln
```

---

## 📖 Documentação

| Documento | Descrição |
|-----------|-----------|
| [Context Diagram](docs/architecture/01-context-diagram.md) | C4 Level 1 — Visão geral do sistema |
| [Container Diagram](docs/architecture/02-container-diagram.md) | C4 Level 2 — Componentes e tecnologias |
| [Component: Transactions](docs/architecture/03-component-transactions.md) | C4 Level 3 — Serviço de Lançamentos |
| [Component: Consolidation](docs/architecture/04-component-consolidation.md) | C4 Level 3 — Serviço de Consolidado |
| [Domain Mapping](docs/architecture/05-domain-mapping.md) | Domínios e bounded contexts |
| [Architectural Patterns](docs/architecture/06-architectural-patterns.md) | Padrões com justificativas e trade-offs |
| [Security Architecture](docs/security/01-security-architecture.md) | Arquitetura de segurança end-to-end |
| [Deployment Strategy](docs/operations/01-deployment-strategy.md) | Blue-Green Deploy, CI/CD, rollback |
| [Monitoring & Observability](docs/operations/02-monitoring-observability.md) | Traces, métricas, logs, alertas |
| [Scaling Strategy](docs/operations/03-scaling-strategy.md) | Cache-first, capacity planning para 50 req/s |
| [Disaster Recovery](docs/operations/04-disaster-recovery.md) | Cenários de falha, RTO/RPO, backups |

---

## 📈 Status de Implementação

| Fase | Entregável | Status |
|------|-----------|--------|
| **Fase 1** | Documentação Arquitetural (C4, ADRs, Padrões) | ✅ Completo |
| **Fase 2** | Documentação de Segurança | ✅ Completo |
| **Fase 3** | Documentação de Operações | ✅ Completo |
| **Fase 4.1** | SharedKernel (entidades, eventos, interfaces, DTOs) | ✅ Completo |
| **Fase 4.2** | Transactions API (JWT Auth + CQRS + MongoDB Outbox) | ✅ Completo |
| **Fase 4.3** | Transactions.Worker (Fast Ingestion + Batch Processing) | ✅ Completo |
| **Fase 4.4** | Consolidation API (JWT Auth + health endpoint) | ✅ Completo |
| **Fase 4.5** | API Gateway (YARP + JWT Auth + Rate Limiting + OTel) | ✅ Completo |
| **Fase 4.6** | Consolidation Worker (MassTransit Consumer + Batch Processing) | ✅ Completo |
| **Fase 5** | Testes Unitários (50 testes: Transactions 18 + Consolidation 20 + Worker 12) | ✅ Completo (40+ testes) |

---

## ✅ Requisito × Evidência — Validação de Escopo

| Requisito | Tipo | Evidência de Implementação | Arquivo |
|-----------|------|---------------------------|---------|
| **RNF-01: Isolamento de Falhas** | Arquitetura | Comunicação exclusivamente assíncrona via RabbitMQ; Transactions API retorna 201 sem depender de Consolidation | `docs/architecture/06-architectural-patterns.md` (Seção 3) |
| **RNF-02: Throughput 50 req/s** | Arquitetura | Cache-First com IMemoryCache (< 10ms HIT), MongoDB para MISS, Rate limiting 100 req/s no Gateway | `docs/architecture/06-architectural-patterns.md` (Seção 2) |
| **RNF-03: Cache em Múltiplas Replicas** | Arquitetura | Padrão Fanout per-Instance com RabbitMQ; cada pod recebe invalidação simultaneamente | `docs/architecture/04-component-consolidation.md` |
| **RN-01: Validação de Valor** | Negócio | FluentValidation em `CreateTransactionRequest`: `amount > 0` | `src/CashFlow.Transactions.API` |
| **RN-02: Cálculo de Saldo** | Negócio | Balance = Sum(Credits) - Sum(Debits); decimal precision no MongoDB | `src/CashFlow.SharedKernel/Domain/Entities` |
| **RN-03: Imutabilidade Transações Passadas** | Negócio | Regra de negócio documentada; endpoints não implementam DELETE/PUT em dados históricos | `docs/requirements/01-functional-requirements.md` (RN-03) |
| **RN-04: Consolidação Diária Única** | Negócio | MongoDB com UPSERT em `daily_balances`; índice único em `(date, userId)` | `src/CashFlow.Consolidation.Worker` |
| **RN-05: Descrição Obrigatória** | Validação | FluentValidation: `!string.IsNullOrWhiteSpace(description) && description.Length <= 500` | `src/CashFlow.Transactions.API` |
| **RN-06: Categoria Vinculada** | Validação | Enum `Category` com valores predefinidos; FluentValidation valida contra enum | `src/CashFlow.SharedKernel/Domain/Enums` |
| **RN-07: Isolamento de Falhas (RabbitMQ)** | Arquitetura | Evento publicado após persistência; Worker processa asincronamente | `docs/architecture/06-architectural-patterns.md` (Seção 3) |
| **RN-08: UserId por Extração JWT** | Segurança | API Gateway extrai `sub` do JWT e injeta header `X-User-Id`; Transactions API usa este header | `src/CashFlow.Gateway/Program.cs` |
| **UC-01: Criar Débito** | Funcional | POST `/api/v1/transactions` com `type: "DEBIT"`; validação, persistência, evento publicado | `src/CashFlow.Transactions.API/Endpoints` |
| **UC-02: Criar Crédito** | Funcional | POST `/api/v1/transactions` com `type: "CREDIT"`; idêntico ao UC-01 | `src/CashFlow.Transactions.API/Endpoints` |
| **UC-03: Consultar Consolidado** | Funcional | GET `/api/v1/consolidation/{date}` via Gateway; cache-first com IMemoryCache | `src/CashFlow.Consolidation.API/Endpoints` |
| **UC-04: Listar Transações** | Funcional | GET `/api/v1/transactions?startDate=...&endDate=...`; paginação, filtros | `src/CashFlow.Transactions.API/Endpoints` |
| **Autenticação JWT** | Segurança | Keycloak emite tokens RS256; Gateway valida assinatura e claims | `src/CashFlow.Gateway/Extensions/ServiceCollectionExtensions.cs` |
| **RBAC (2 papéis)** | Segurança | `admin` pode criar (POST /transactions); `user` pode ler (GET); políticas no Gateway | `src/CashFlow.Gateway/Program.cs` + ADR-009 |
| **Rate Limiting** | Performance | YARP: 100 req/s por IP (global); Consolidation API: 50 req/s (dedicado) | `src/CashFlow.Gateway` + `src/CashFlow.Consolidation.API` |
| **Observabilidade** | Operações | OpenTelemetry correlationId em todos os traces; Seq para logs estruturados; Jaeger para distributed tracing | `src/***/Extensions/ServiceCollectionExtensions.cs` |
| **Testes Unitários** | QA | 50 testes `[Fact]`: Transactions 18 + Consolidation 20 + Worker 12; todos com `[Fact]` e naming idiomático | `tests/CashFlow.*.Tests` |

---

## 🔑 Credenciais Locais (desenvolvimento)

| Serviço | URL | Usuário | Senha |
|---------|-----|---------|-------|
| Keycloak | http://localhost:8080 | admin | admin |
| RabbitMQ Management | http://localhost:15672 | guest | guest |
| Grafana | http://localhost:3000 | admin | admin |
| Jaeger | http://localhost:16686 | — | — |

---

📅 **Última atualização:** Março 2026  
🔧 **Desafio:** Arquiteto de Soluções — Sistema de Controle de Fluxo de Caixa
