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
| Consolidado suporta **50 req/s com ≤ 5% de perda** | **Cache-First com Redis** (TTL 5min) + horizontal scaling |

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
└─────────────┬────────────────────────────┘
              │
    ┌─────────┴──────────┐
    │                    │
    ▼                    ▼
┌──────────┐    ┌────────────────┐
│Transactions│  │  Consolidation │
│   API    │    │     API        │
│ :8081    │    │    :8082       │
└────┬─────┘    └───────┬────────┘
     │                  │
     │ MongoDB Outbox    │ Cache-First
     │ Transaction       │ (Redis TTL 5min)
     ▼ Created           │
┌──────────┐             │
│ RabbitMQ │             │ Cache MISS
└────┬─────┘             ▼
     │          ┌──────────────────┐
     │          │   MongoDB        │
     │          │ consolidation_db │
     ▼          └──────────────────┘
┌─────────────────┐
│  Consolidation  │
│     Worker      │  ← Upsert + Invalidate Cache
│  (MassTransit)  │
└─────────────────┘
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
| **Cache-First (Redis)** | Consolidation API | Latência < 50ms, suporte a 50+ req/s |
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
| **Cache** | Redis 7.2 | 2.7.10 (StackExchange) | Cache-first, TTL 5min, < 50ms p95 |
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
- Cache-First com Redis (TTL 5min) → p95 < 50ms no happy path
- Cache miss → MongoDB (200-500ms)
- Horizontal scaling via réplicas stateless
- Rate limiting no API Gateway (100 req/s por IP)

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
| **Fase 4.6** | Consolidation Worker | 🔄 Planejado |
| **Fase 5** | Testes Unitários e de Integração | 🔄 Planejado |

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
