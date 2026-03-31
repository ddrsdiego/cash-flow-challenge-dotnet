# 01 — Security Architecture

## Visão Geral

Este documento apresenta a **arquitetura de segurança do CashFlow System**, descrevendo todos os pontos de controle, as camadas de proteção e o modelo de ameaças considerado.

A segurança não é um componente isolado — é uma propriedade transversal que permeia cada camada da solução: rede, gateway, serviços, dados e operação.

---

## Princípios de Segurança Adotados

| Princípio | Como se Manifesta no Sistema |
|-----------|------------------------------|
| **Defense in Depth** | Múltiplas camadas independentes — comprometer uma não implica comprometer todas |
| **Least Privilege** | Roles RBAC granulares; serviços internos sem exposição pública |
| **Zero Trust (parcial)** | Identidade verificada no Gateway; headers de identidade não aceitos de fora |
| **Fail Secure** | Requisição sem token válido → 401; token sem role → 403 |
| **Auditabilidade** | Toda ação relevante logada com `userId`, `traceId`, `timestamp` |
| **Separação de Responsabilidades** | Autenticação no IdP, autorização no Gateway, negócio nos serviços |

---

## Diagrama de Segurança (Pontos de Controle)

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                          INTERNET / CLIENTE                                     │
│                                                                                 │
│    ┌────────────────────────────────────────────────────────────┐               │
│    │ Comerciante / REST Client                                  │               │
│    │  1. Autentica no Keycloak → obtém JWT                      │               │
│    │  2. Envia requests com Authorization: Bearer {token}       │               │
│    └──────────────────────────────┬─────────────────────────────┘               │
└─────────────────────────────────────────────────────────────────────────────────┘
                                    │ HTTPS (TLS 1.3)
                                    │ [CONTROLE 1: Criptografia em Trânsito]
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  FRONTEND NETWORK (frontend-net)                                                │
│                                                                                 │
│  ┌──────────────────────────────────────────────────────────────────────────┐   │
│  │  API GATEWAY (YARP)                                                      │   │
│  │                                                                          │   │
│  │  [CONTROLE 2] Rate Limiting         → 100 req/s por IP (global)          │   │
│  │  [CONTROLE 3] JWT Validation        → valida assinatura + expiração      │   │
│  │  [CONTROLE 4] RBAC Check            → verifica roles no token            │   │
│  │  [CONTROLE 5] Security Headers      → HSTS, X-Frame-Options etc.         │   │
│  │  [CONTROLE 6] X-User-Id Injection   → extrai sub do JWT, injeta header   │   │
│  │  [CONTROLE 7] Request Logging       → audit trail de todas as chamadas   │   │
│  │                                                                          │   │
│  └──────────────┬──────────────────────────────────────────┬────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────────┘
                  │ HTTP interno                              │ HTTP interno
                  │ [rede isolada - backend-net]             │ [rede isolada - backend-net]
                  ▼                                          ▼
┌─────────────────────────────────────────────────────────────────────────────────┐
│  BACKEND NETWORK (backend-net) — SEM EXPOSIÇÃO PÚBLICA                          │
│                                                                                 │
│  ┌─────────────────────┐              ┌─────────────────────────────────────┐   │
│  │  Transactions API   │              │  Consolidation API                  │   │
│  │                     │              │                                     │   │
│  │ [C8] X-User-Id      │              │ [C10] Rate Limit endpoint           │   │
│  │      obrigatório    │              │       50 req/s                      │   │
│  │ [C9] Input Validation│             │ [C11] Input Validation (date)       │   │
│  │      FluentValidation│             │                                     │   │
│  │                     │  AMQP        │                                     │   │
│  │    ──────────────────┼──────────►  │  Consolidation Worker               │   │
│  │                     │  RabbitMQ    │  [C12] Idempotência (evita replay)  │   │
│  └─────────────────────┘              └─────────────────────────────────────┘   │
│                                                                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐    │
│  │  DATA STORES                                                            │    │
│  │                                                                         │    │
│  │  MongoDB [C13] Autenticação obrigatória (user/password)                 │    │
│  │          [C14] Encryption at rest (desabilitado no dev, ativo em prod)  │    │
│  │          [C15] Rede isolada (backend-net apenas)                        │    │
│  │                                                                         │    │
│  │  Redis   [C16] requirepass (autenticação obrigatória)                   │    │
│  │          [C17] Rede isolada (backend-net apenas)                        │    │
│  │          [C18] AOF persistence (integridade de dados)                   │    │
│  │                                                                         │    │
│  │  RabbitMQ[C19] Autenticação AMQP (user/password)                        │    │
│  │          [C20] Rede isolada (backend-net apenas)                        │    │
│  └─────────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────────┐
│  IDENTITY PROVIDER NETWORK                                                      │
│                                                                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐    │
│  │  Keycloak                                                               │    │
│  │  [C21] Emite e assina JWTs (RS256)                                      │    │
│  │  [C22] Gerencia ciclo de vida de tokens (expiração, refresh, revogação) │    │
│  │  [C23] Armazena usuários e roles com hash de senha (bcrypt)             │    │
│  └─────────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────────┘
```

---

## Inventário de Controles de Segurança

| ID | Camada | Controle | Tecnologia | Detalhe |
|----|--------|----------|-----------|---------|
| C1 | Rede | TLS 1.3 obrigatório para comunicação externa | Reverse proxy / TLS termination | Certificado CA confiável em produção |
| C2 | Gateway | Rate limiting global | YARP + ASP.NET Core Rate Limiter | 100 req/s por IP; 429 com `Retry-After` |
| C3 | Gateway | Validação de JWT | Microsoft.AspNetCore.Authentication.JwtBearer | Verifica assinatura, emissor, audiência e expiração |
| C4 | Gateway | RBAC com papéis básicos | ASP.NET Core `RequireAuthorization("require-admin/require-user")` | 2 papéis: `admin` (criar), `user` (ler); ver ADR-009 |
| C5 | Gateway | Security headers | YARP middleware customizado | HSTS, X-Content-Type-Options, X-Frame-Options, Referrer-Policy |
| C6 | Gateway | Propagação de identidade | Header `X-User-Id` injetado pelo Gateway | Extrai claim `sub` do JWT validado; ver ADR-003 |
| C7 | Gateway | Audit logging | OpenTelemetry + Seq | Toda requisição logada com userId, IP, endpoint, status |
| C8 | Serviço | Validação de header de identidade | ASP.NET Core middleware | Transactions API rejeita requisições sem `X-User-Id` |
| C9 | Serviço | Validação de input | FluentValidation | amount > 0, description não vazio, category válida, date não futura |
| C10 | Serviço | Rate limit por endpoint | ASP.NET Core Rate Limiter | Consolidation API: 50 req/s |
| C11 | Serviço | Validação de input | FluentValidation | date não pode ser futura; formato YYYY-MM-DD |
| C12 | Serviço | Idempotência | MongoDB (índice único em idempotencyKey) | Evita processamento duplicado de eventos at-least-once |
| C13 | Dados | Autenticação MongoDB | MongoDB URI com user/password | Configurado via variável de ambiente; sem acesso anônimo |
| C14 | Dados | Encryption at rest MongoDB | MongoDB Enterprise / FS encryption | Configurado em produção; desabilitado em dev por simplicidade |
| C15 | Dados | Isolamento de rede MongoDB | Docker network `backend-net` | MongoDB não exposto externamente (sem port binding público) |
| C16 | Dados | Cache in-process | IMemoryCache (.NET) | TTL 5min; per-instance (não compartilhado entre replicas); ver ADR-008 |
| C17 | Dados | Isolamento de rede | Docker network `backend-net` | Consolidation API não exposta externamente |
| C18 | Dados | Cache invalidation | Event-driven (DailyConsolidationUpdatedEvent) | Worker invalida cache após update |
| C19 | Dados | Autenticação RabbitMQ | AMQP user/password | Usuário e senha configurados via variáveis de ambiente |
| C20 | Dados | Isolamento de rede RabbitMQ | Docker network `backend-net` | Management UI exposta apenas em dev (porta 15672) |
| C21 | Identity | Emissão de tokens JWT | Keycloak RS256 | Tokens assinados com chave privada; Gateway valida com chave pública |
| C22 | Identity | Ciclo de vida de tokens | Keycloak | Access token: 1h; Refresh token: 7d; Revogação via Keycloak admin |
| C23 | Identity | Armazenamento seguro de credenciais | Keycloak + PostgreSQL | Senhas com bcrypt; PostgreSQL com autenticação obrigatória |

---

## Camadas de Segurança

A arquitetura adota **Defense in Depth** com quatro camadas independentes:

```
┌──────────────────────────────────────────────────┐
│  CAMADA 4: DADOS                                 │
│  Autenticação, encryption at rest, rede isolada  │
├──────────────────────────────────────────────────┤
│  CAMADA 3: SERVIÇOS                              │
│  Input validation, identity check, idempotência  │
├──────────────────────────────────────────────────┤
│  CAMADA 2: GATEWAY                               │
│  JWT, RBAC, rate limit, security headers         │
├──────────────────────────────────────────────────┤
│  CAMADA 1: REDE                                  │
│  TLS 1.3, isolamento Docker, sem exposição direta│
└──────────────────────────────────────────────────┘
```

**Por que múltiplas camadas?** Se uma camada falha (ex: bypass do Gateway), as camadas internas ainda protegem. Um atacante que consiga bypassar o Gateway ainda encontrará:
- Serviços que rejeitam requisições sem `X-User-Id` (C8)
- Validação de input nos serviços (C9, C11)
- Bancos de dados com autenticação própria (C13, C16, C19)
- Rede isolada que impede acesso externo direto (C15, C17, C20)

---

## Modelo de Ameaças (STRIDE)

### Ativos a Proteger

| Ativo | Criticidade | Justificativa |
|-------|-------------|---------------|
| Dados financeiros (transações) | 🔴 Alta | Informação financeira sensível do negócio |
| Credenciais de usuários | 🔴 Alta | Acesso ao sistema inteiro |
| Saldo diário consolidado | 🟡 Média | Dado de negócio; leitura não autorizada é relevante |
| Configuração e secrets | 🔴 Alta | Comprometimento total do sistema |
| Logs de auditoria | 🟡 Média | Imutabilidade importante para compliance |

### Análise de Ameaças STRIDE

| Ameaça | Vetor | Controles de Mitigação | Risco Residual |
|--------|-------|----------------------|----------------|
| **Spoofing** (forja de identidade) | Criar lançamentos com `userId` falso no body | C3 (JWT valida usuário), C6 (userId extraído pelo Gateway), C8 (header obrigatório) | Baixo |
| **Tampering** (adulteração de dados) | Modificar transações em trânsito | C1 (TLS 1.3); imutabilidade por design (sem endpoint de edição) | Baixo |
| **Repudiation** (negação de ação) | Usuário negar que criou lançamento | C7 (audit log com userId), C6 (userId extraído de JWT não forjável) | Baixo |
| **Information Disclosure** (exposição de dados) | Leitura de transações de outros usuários | C3 (autenticação obrigatória), C4 (RBAC), C13-C20 (dados isolados) | Médio (MVP single-tenant) |
| **Denial of Service** | Flood de requisições | C2 (rate limiting 100 req/s), C10 (50 req/s no consolidado) | Médio (single-node, sem CDN) |
| **Elevation of Privilege** | Acessar endpoints sem permissão | C4 (RBAC com 2 papéis: admin/user), C3 (JWT inválido = 401) | Baixo (RBAC implementado per ADR-009) |

### Riscos Residuais Documentados

| Risco | Causa | Mitigação Futura |
|-------|-------|-----------------|
| **Information Disclosure (médio)** | MVP é single-tenant — sem isolamento de dados por usuário | Introduzir `merchantId` para isolamento multi-tenant em versão futura |
| **Elevation of Privilege (médio)** | MVP sem RBAC granular — apenas autenticação obrigatória | Implementar RBAC roles (`transactions:read/write`, `consolidation:read`) em Phase 2; ver ADR-009 |
| **Cache não compartilhado (médio)** | IMemoryCache é per-instance em MVP | Migrar para Redis em cluster em produção (Phase 2); ver ADR-008 |
| **DoS no nível de infraestrutura** | RabbitMQ e MongoDB são single-node no MVP | Cluster de alta disponibilidade em produção; CDN para absolver pico |
| **Secrets em arquivos `.env`** | `.env` no ambiente dev pode ser exposto | Em produção: AWS Secrets Manager / Azure Key Vault / HashiCorp Vault |
| **Keycloak sem HA** | Single-node no MVP | Cluster Keycloak em produção para evitar SPOF de autenticação |

---

## Superfície de Ataque

### Exposto Externamente (Internet-facing)

| Endpoint | Protocolo | Autenticação | Rate Limit |
|----------|-----------|-------------|------------|
| `GET/POST /api/v1/transactions*` via API Gateway | HTTPS | JWT obrigatório | 100 req/s global |
| `GET /api/v1/consolidation/*` via API Gateway | HTTPS | JWT obrigatório | 100 req/s global |
| `POST /auth/token` via Keycloak | HTTPS | username + password | Keycloak built-in |

### Exposto para Observabilidade (Acesso restrito)

| Endpoint | Porta | Restrição |
|----------|-------|-----------|
| Grafana UI | 3000 | Acesso dev/admin; proteger em produção com VPN |
| Jaeger UI | 16686 | Acesso dev/admin; proteger em produção com VPN |
| Seq UI | 8341 | Acesso dev/admin; proteger em produção com VPN |
| RabbitMQ Management | 15672 | Apenas dev; desabilitar em produção |

### Não Exposto (Isolado em backend-net)

- Transactions API (porta 8081) — apenas via Gateway
- Transactions.Worker (sem porta HTTP) — apenas consume mensagens
- Consolidation API (porta 8082) — apenas via Gateway
- Consolidation.Worker (sem porta HTTP) — apenas consume mensagens
- MongoDB (porta 27017) — apenas entre serviços e workers
- RabbitMQ AMQP (porta 5672) — apenas entre serviços e workers

---

## Compliance e Auditoria

### Requisitos Atendidos

| Requisito | Controle |
|-----------|---------|
| LGPD — Identificação do responsável pelo dado | `userId` persistido em cada Transaction (ver ADR-003) |
| LGPD — Rastreabilidade de acesso | Audit log com userId, IP, endpoint, timestamp (C7) |
| Compliance financeiro — Imutabilidade | Sem endpoint de edição de transações passadas (> 24h) |
| Compliance financeiro — Retenção | Dados financeiros: 7 anos (MongoDB com retenção sem TTL); logs: 30 dias (Seq) |

---

## Referências Internas

| Documento | Conteúdo |
|-----------|---------|
| `docs/security/02-authentication-authorization.md` | Fluxo OAuth2, JWT, RBAC — detalhamento completo |
| `docs/security/03-api-protection.md` | Rate limiting, input validation, CORS, security headers |
| `docs/security/04-data-protection.md` | TLS, encryption at rest, gestão de secrets, mascaramento |
| `docs/decisions/ADR-003-user-context-propagation.md` | Como `userId` é extraído do JWT e propagado |
| `docs/decisions/ADR-004-api-gateway.md` | Justificativa da escolha do YARP como API Gateway |
| `docs/decisions/ADR-005-authentication-strategy.md` | Justificativa da escolha do Keycloak como IdP |
| `docs/requirements/02-non-functional-requirements.md` | Seção 3 — Segurança; Seção 6.3 — Auditoria |

---

**Próximo documento:** `docs/security/02-authentication-authorization.md`
