# Plano de Implementação — Desafio Técnico Arquiteto de Soluções

## Visão Geral do Projeto

**Domínio:** Sistema de Controle de Fluxo de Caixa para Comerciantes
**Stack:** C# / .NET 8
**Abordagem:** Documentação primeiro, código depois

---

## Análise do Desafio

### O Problema
Um comerciante precisa de dois serviços desacoplados:
1. **Controle de Lançamentos** — registrar débitos e créditos no fluxo de caixa
2. **Consolidado Diário** — gerar relatório com saldo diário consolidado

### Requisitos Não Funcionais Críticos
- **Resiliência:** O serviço de lançamentos NÃO pode ficar indisponível se o consolidado falhar
- **Throughput:** Consolidado deve suportar 50 req/s com no máximo 5% de perda

### Implicações Arquiteturais Diretas
| Requisito | Decisão Arquitetural |
|-----------|---------------------|
| Lançamentos não pode cair com consolidado | Comunicação assíncrona via message broker (serviços desacoplados) |
| 50 req/s com ≤5% perda | Cache de leitura + fila com retry + horizontal scaling |
| Segurança obrigatória | API Gateway + OAuth2/JWT + mTLS entre serviços |

---

## Estrutura do Repositório

```
cashflow-system/
├── README.md
├── docker-compose.yml
├── docs/
│   ├── architecture/
│   │   ├── 01-context-diagram.md          # C4 Level 1
│   │   ├── 02-container-diagram.md        # C4 Level 2
│   │   ├── 03-component-transactions.md   # C4 Level 3 - Lançamentos
│   │   ├── 04-component-consolidation.md  # C4 Level 3 - Consolidado
│   │   ├── 05-domain-mapping.md           # Domínios e capacidades
│   │   └── 06-architectural-patterns.md   # Padrões adotados
│   ├── security/
│   │   ├── 01-security-architecture.md
│   │   ├── 02-authentication-authorization.md
│   │   ├── 03-api-protection.md
│   │   └── 04-data-protection.md
│   ├── decisions/
│   │   ├── ADR-001-async-communication.md
│   │   ├── ADR-002-database-per-service.md
│   │   ├── ADR-003-cqrs-consolidation.md
│   │   ├── ADR-004-api-gateway.md
│   │   ├── ADR-005-authentication-strategy.md
│   │   └── ADR-006-observability-stack.md
│   ├── operations/
│   │   ├── 01-deployment-strategy.md
│   │   ├── 02-monitoring-observability.md
│   │   ├── 03-scaling-strategy.md
│   │   └── 04-disaster-recovery.md
│   └── requirements/
│       ├── 01-functional-requirements.md
│       └── 02-non-functional-requirements.md
├── src/
│   ├── CashFlow.Transactions.API/        # Serviço de Lançamentos
│   ├── CashFlow.Consolidation.API/       # Serviço de Consolidado
│   ├── CashFlow.Consolidation.Worker/    # Worker de processamento
│   ├── CashFlow.Gateway/                 # API Gateway (YARP)
│   └── CashFlow.SharedKernel/            # Contratos e abstrações
├── tests/
│   ├── CashFlow.Transactions.Tests/
│   ├── CashFlow.Consolidation.Tests/
│   └── CashFlow.Integration.Tests/
└── infra/
    ├── docker/
    ├── k8s/                              # Manifests Kubernetes
    └── scripts/
```

---

## Arquitetura Proposta (Resumo)

### Padrões Arquiteturais
- **Microservices** com comunicação assíncrona via RabbitMQ
- **CQRS** no serviço de consolidado (separação leitura/escrita)
- **Database per Service** (isolamento de dados)
- **API Gateway** com YARP para roteamento e proteção
- **Event-Driven** para desacoplamento entre lançamentos e consolidação

### Stack Tecnológica
| Componente | Tecnologia | Justificativa |
|-----------|-----------|--------------|
| APIs | .NET 8 Minimal APIs | Performance, AOT-ready, ecossistema maduro |
| Gateway | YARP (Yet Another Reverse Proxy) | Nativo .NET, alta performance, extensível |
| Broker | RabbitMQ | Confiável, dead-letter queues, confirmação de entrega |
| DB Lançamentos | MongoDB 7.0 | NoSQL flexível, TTL indexes para dados temporários, ideal para event sourcing light |
| DB Consolidado | MongoDB 7.0 + Redis (cache) | Cache em memória para leitura rápida do consolidado, TTL 5min |
| Observabilidade | OpenTelemetry + Seq + Grafana + Jaeger | Tracing distribuído (Jaeger), logs centralizados (Seq), métricas (Prometheus/Grafana) |
| Auth | Keycloak (Identity Provider) | OAuth2/OIDC completo, open-source, suporte a RBAC |
| Containers | Docker + Docker Compose | Reprodutibilidade, deploy simplificado, multi-stage builds |

---

## Fases de Implementação

### FASE 1 — Documentação Arquitetural (Prioridade Máxima)
> **Objetivo:** Comunicar decisões com clareza e profundidade

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 1.1 | Mapeamento de Domínios | Domínios funcionais, bounded contexts, capacidades de negócio | 1 sessão |
| 1.2 | Requisitos Refinados | Funcionais detalhados + não funcionais com métricas | 1 sessão |
| 1.3 | C4 Context Diagram | Visão geral: comerciante, sistema, integrações externas | 1 sessão |
| 1.4 | C4 Container Diagram | APIs, broker, databases, gateway, identity provider | 1 sessão |
| 1.5 | C4 Component Diagrams | Componentes internos de cada serviço | 1 sessão |
| 1.6 | ADRs (Architecture Decision Records) | 6 ADRs fundamentando cada decisão relevante | 1 sessão |
| 1.7 | Fluxos de Interação | Sequência de lançamento e consulta de consolidado | 1 sessão |

### FASE 2 — Documentação de Segurança (Obrigatória)
> **Objetivo:** Demonstrar visão de segurança end-to-end

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 2.1 | Arquitetura de Segurança | Diagrama de segurança com todos os pontos de controle | 1 sessão |
| 2.2 | Autenticação/Autorização | OAuth2 + JWT + RBAC detalhado | 1 sessão |
| 2.3 | Proteção de APIs | Rate limit, validação, CORS, headers de segurança | 1 sessão |
| 2.4 | Proteção de Dados | Criptografia em trânsito (TLS/mTLS) e em repouso, mascaramento | 1 sessão |

### FASE 3 — Documentação de Operação
> **Objetivo:** Mostrar maturidade operacional

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 3.1 | Estratégia de Deploy | Blue-green, containers, CI/CD pipeline | 1 sessão |
| 3.2 | Observabilidade | Métricas, traces, logs, dashboards, alertas | 1 sessão |
| 3.3 | Escalabilidade | Auto-scaling, capacity planning para 50 req/s | 1 sessão |
| 3.4 | Recuperação de Falhas | Circuit breaker, retry, dead-letter, fallback | 1 sessão |

### FASE 4 — Implementação do Código
> **Objetivo:** MVP funcional com testes que comprove a arquitetura

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 4.1 | SharedKernel | Contratos, DTOs, eventos, abstrações, domain models | 1 sessão |
| 4.2 | Transactions API | POST /transactions + MongoDB insert + Outbox pattern + RabbitMQ publish | 1-2 sessões |
| 4.3 | Consolidation Worker | BackgroundService que consome TransactionCreated + recalcula saldo + invalida Redis | 1-2 sessões |
| 4.4 | Consolidation API | GET /consolidation/daily (cache-first, fallback a MongoDB) | 1 sessão |
| 4.5 | API Gateway | Configuração YARP + auth middleware (JWT) + rate limiting + roteamento | 1 sessão |
| 4.6 | Docker Compose | Orquestração (apps, MongoDB, Redis, RabbitMQ, Keycloak, OTel stack) + health checks | ✅ Já feito |

### FASE 5 — Testes e Qualidade
> **Objetivo:** Cobertura automatizada

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 5.1 | Testes Unitários | Domain logic, validações, cálculos | 1 sessão |
| 5.2 | Testes de Integração | API endpoints, mensageria, persistência | 1 sessão |
| 5.3 | README Final | Instruções de execução, pré-requisitos, arquitetura | 1 sessão |

### FASE 6 — Diferenciais (Bônus)
> **Objetivo:** Destacar-se dos demais candidatos

| # | Entregável | Descrição | Estimativa |
|---|-----------|-----------|-----------|
| 6.1 | Estimativa de Custos | Custos AWS/Azure para a infraestrutura proposta | 1 sessão |
| 6.2 | Arquitetura de Transição | Migração de legado hipotética (planilha → sistema) | 1 sessão |
| 6.3 | Health Checks e Readiness | Endpoints de saúde para orquestração | Incluído na fase 4 |

---

## Ordem de Execução Recomendada

```
Sessão 01 → 1.1 Mapeamento de Domínios + 1.2 Requisitos
Sessão 02 → 1.3 C4 Context + 1.4 C4 Container
Sessão 03 → 1.5 C4 Components + 1.7 Fluxos de Interação
Sessão 04 → 1.6 ADRs (todos os 6)
Sessão 05 → 2.1 Arquitetura de Segurança + 2.2 Auth
Sessão 06 → 2.3 Proteção APIs + 2.4 Proteção Dados
Sessão 07 → 3.1 Deploy + 3.2 Observabilidade
Sessão 08 → 3.3 Escalabilidade + 3.4 Recuperação de Falhas
Sessão 09 → 4.1 SharedKernel + 4.2 Transactions API
Sessão 10 → 4.3 Consolidation Worker + 4.4 Consolidation API
Sessão 11 → 4.5 Gateway + 4.6 Docker Compose
Sessão 12 → 5.1 Testes Unitários + 5.2 Testes Integração
Sessão 13 → 5.3 README + 6.1 Custos + 6.2 Transição
```

---

## Critérios de Sucesso

O entregável final será avaliado em 5 dimensões. Nosso plano endereça todas:

| Dimensão | Como Endereçamos |
|---------|-----------------|
| **Maturidade Arquitetural** | C4 completo, domain mapping, CQRS, event-driven, database-per-service |
| **Comunicação Técnica** | Diagramas estruturados, ADRs formais, documentação organizada em /docs |
| **Visão de Segurança** | Seção dedicada com auth, proteção de APIs, criptografia, mTLS |
| **Fundamentação de Decisões** | 6+ ADRs com trade-offs, alternativas descartadas e justificativas |
| **Visão Operacional** | Deploy strategy, observabilidade com OpenTelemetry, scaling, DR |

---

## Priorização Explícita

### Dentro do Escopo (MVP)
- Dois serviços desacoplados (lançamentos + consolidado)
- Comunicação assíncrona via RabbitMQ
- API Gateway com autenticação
- Consolidado diário com cache
- Testes unitários e de integração
- Docker Compose para execução local
- Documentação completa

### Fora do Escopo Inicial (documentado como evolução)
- Interface gráfica (frontend)
- Multi-tenancy (múltiplos comerciantes com isolamento)
- Relatórios avançados (mensal, por categoria)
- CI/CD pipeline real (será documentado mas não implementado)
- Kubernetes em produção (manifests de referência apenas)
- Notificações push/email

### Riscos e Limitações
- RabbitMQ single-node no Docker Compose (produção precisa de cluster)
- Keycloak simplificado (realm básico para demonstração)
- Sem load testing real (documentaremos o approach com k6/locust)
- PostgreSQL sem réplica de leitura (produção precisaria de read replicas)