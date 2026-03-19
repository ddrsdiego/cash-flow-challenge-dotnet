# Entendimento da Tarefa — Sessão 04

## Referência
Sessão anterior: `.arquitetura/sessao-03/entendimento.md`  
Plano: `docs/plano-implementacao.md` (FASE 1, item 1.6)

---

## Contexto

Sessões anteriores concluídas:
- ✅ **Sessão 01** — `01-functional-requirements.md`, `02-non-functional-requirements.md`, `05-domain-mapping.md`
- ✅ **Sessão 02** — `01-context-diagram.md` (C4 Level 1), `02-container-diagram.md` (C4 Level 2)
- ✅ **Sessão 03** — `03-component-transactions.md`, `04-component-consolidation.md`, `06-architectural-patterns.md`

**Estado do projeto:**
- ✅ Infraestrutura (docker-compose.yml) — Completa
- ✅ Requisitos — Funcionais e não funcionais documentados
- ✅ Domain mapping — 2 bounded contexts com domain events
- ✅ C4 Level 1, Level 2 e Level 3 — Context, Container e Component diagrams
- ✅ Padrões arquiteturais — Outbox, Cache-First, Event-Driven, Idempotência, DLQ, CQRS Light, Circuit Breaker, API Gateway
- ❌ ADRs — Esta sessão
- ❌ Código-fonte — Sessão 09+

---

## Tarefa

Produzir os 6 Architecture Decision Records (ADRs) em `docs/decisions/`:

| # | Arquivo | Decisão |
|---|---------|---------|
| 1 | `ADR-001-async-communication.md` | Comunicação assíncrona via RabbitMQ + Outbox Pattern |
| 2 | `ADR-002-database-per-service.md` | Database-per-Service com MongoDB |
| 3 | `ADR-003-cqrs-consolidation.md` | CQRS Light no Consolidation Service |
| 4 | `ADR-004-api-gateway.md` | API Gateway com YARP |
| 5 | `ADR-005-authentication-strategy.md` | Autenticação com Keycloak + OAuth2/OIDC + JWT |
| 6 | `ADR-006-observability-stack.md` | Stack de Observabilidade com OpenTelemetry |

---

## Formato dos ADRs

Padrão **MADR (Markdown Architectural Decision Records)** com seções:
- **Status** — Accepted / Deprecated / Superseded
- **Contexto** — Problema motivador + constraints
- **Drivers de Decisão** — Requisitos que guiaram a escolha
- **Opções Consideradas** — Alternativas avaliadas
- **Decisão** — O que foi escolhido e por quê
- **Análise Comparativa** — Tabela de trade-offs entre opções
- **Consequências** — Positivas, negativas (trade-offs aceitos), riscos, ADRs relacionadas

---

## Objetivo

Documentar formalmente **por que** cada decisão arquitetural foi tomada, incluindo:
- ✅ Contexto e motivação real (não apenas "usamos RabbitMQ porque é bom")
- ✅ Alternativas consideradas e por que foram descartadas
- ✅ Trade-offs aceitos conscientemente
- ✅ Consequências e decisões que dependem desta
- ✅ Ligação explícita com requisitos não funcionais

---

## Abordagem da Sessão

**Uma ADR por vez** — cada ADR é revisada, ajustada se necessário e confirmada antes de avançar para a próxima.

Ordem de produção:
1. → **ADR-001**: Comunicação Assíncrona (esta sessão)
2. → **ADR-002**: Database-per-Service (próximo contexto)
3. → **ADR-003**: CQRS Light (próximo contexto)
4. → **ADR-004**: API Gateway (próximo contexto)
5. → **ADR-005**: Autenticação (próximo contexto)
6. → **ADR-006**: Observabilidade (próximo contexto)

---

## Escopo

### IN SCOPE ✅
- 6 ADRs com formato MADR completo
- Referências cruzadas entre ADRs
- Ligação explícita com requisitos não funcionais documentados
- Registro desta sessão

### OUT OF SCOPE ❌
- ADRs de segurança detalhada (Sessão 05/06)
- ADRs de deploy e operação (Sessão 07/08)
- Código-fonte (Sessão 09+)

---

**Status:** ✅ Entendimento validado. Implementação iniciada.

---

*Próximas ações após esta sessão:*
1. ✅ Criar `ADR-001-async-communication.md`
2. → Sessão seguinte: `ADR-002-database-per-service.md`
3. → Sessão seguinte: `ADR-003-cqrs-consolidation.md`
4. → Sessão seguinte: `ADR-004-api-gateway.md`
5. → Sessão seguinte: `ADR-005-authentication-strategy.md`
6. → Sessão seguinte: `ADR-006-observability-stack.md`
7. → Sessão 05: Documentação de Segurança
