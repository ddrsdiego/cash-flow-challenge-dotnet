# Entendimento da Tarefa — Sessão 03

## Referência
Sessão anterior: `.arquitetura/sessao-02/entendimento.md`  
Plano: `docs/plano-implementacao.md` (FASE 1, items 1.5 e 1.7)

---

## Contexto

Sessões anteriores concluídas:
- ✅ **Sessão 01** — `01-functional-requirements.md`, `02-non-functional-requirements.md`, `05-domain-mapping.md`
- ✅ **Sessão 02** — `01-context-diagram.md` (C4 Level 1), `02-container-diagram.md` (C4 Level 2)

**Estado do projeto:**
- ✅ Infraestrutura (docker-compose.yml) — Completa
- ✅ Requisitos — Funcionais e não funcionais documentados
- ✅ Domain mapping — 2 bounded contexts com domain events
- ✅ C4 Level 1 e Level 2 — Context e Container diagrams criados
- ❌ C4 Level 3 (Component Diagrams) — Esta sessão
- ❌ Fluxos de sequência — Esta sessão
- ❌ Código-fonte — Sessão 09+

---

## Tarefa

Produzir os diagramas de componentes internos de cada serviço e os padrões arquiteturais adotados:

| # | Arquivo | Conteúdo | Padrão |
|---|---------|---------|--------|
| 1 | `docs/architecture/03-component-transactions.md` | Componentes internos do Transactions Service | C4 Level 3 |
| 2 | `docs/architecture/04-component-consolidation.md` | Componentes do Consolidation Service + Worker | C4 Level 3 |
| 3 | `docs/architecture/06-architectural-patterns.md` | Padrões arquiteturais com justificativas e trade-offs | Referência |

---

## Objetivo

Comunicar a estrutura interna de cada serviço com:
- ✅ Componentes e suas responsabilidades
- ✅ Relações e dependências entre componentes
- ✅ Padrões aplicados (Outbox, Cache-First, DLQ, Idempotência)
- ✅ Fluxos de dados passo-a-passo (sequence diagrams)

---

## Escopo

### IN SCOPE ✅

**03 — Transactions Service Components:**
- `TransactionEndpoints` — Minimal API route definitions
- `TransactionService` — Application service (orquestra validação + persistência + evento)
- `TransactionValidator` — FluentValidation rules
- `Transaction` — Domain aggregate root
- `Category` — Value object (enum)
- `ITransactionRepository` / `MongoTransactionRepository`
- `IOutboxRepository` / `MongoOutboxRepository`
- `OutboxPublisher` — Background service (Outbox Pattern)
- Sequence diagrams: criar lançamento (happy path + falha)

**04 — Consolidation Service + Worker Components:**

*Consolidation API:*
- `ConsolidationEndpoints` — Minimal API route definitions
- `ConsolidationService` — Application service (cache-first)
- `IConsolidationCache` / `RedisConsolidationCache`
- `IConsolidationRepository` / `MongoConsolidationRepository`
- `DailyConsolidation` — Domain aggregate

*Consolidation Worker:*
- `TransactionCreatedConsumer` — RabbitMQ consumer (Background Service)
- `IdempotencyChecker` — Verifica se evento já foi processado
- `ConsolidationCalculator` — Domain service (calcula saldo)
- `ITransactionReader` / `MongoTransactionReader`
- `ICacheInvalidator` / `RedisCacheInvalidator`
- Sequence diagrams: processar evento + consultar saldo (cache hit/miss)

**06 — Architectural Patterns:**
- Outbox Pattern — Garantia de publicação
- Cache-First Pattern — Leitura rápida com fallback
- At-Least-Once + Idempotência — Processar sem duplicar
- Dead Letter Queue — Retry e fallback
- Database-per-Service — Isolamento de dados
- Circuit Breaker — Resiliência de dependências

### OUT OF SCOPE ❌

- ❌ Código-fonte real — Sessão 09+
- ❌ ADRs — Sessão 04
- ❌ Segurança detalhada — Sessão 05/06
- ❌ Operação e deploy — Sessão 07/08

---

## Formato dos Diagramas

- **C4 Component:** Mermaid `C4Component` (renderiza no GitHub)
- **Sequence Diagrams:** Mermaid `sequenceDiagram` (renderiza no GitHub)

---

## Estrutura Arquitetural Esperada

### Transactions Service (Clean Architecture Simplificado)
```
TransactionEndpoints  ←── HTTP (Minimal API)
     ↓
TransactionService  ←── Application Layer
     ↓              ↓
TransactionValidator  ITransactionRepository ─── MongoTransactionRepository ─── MongoDB
                          IOutboxRepository ─── MongoOutboxRepository

OutboxPublisher (background) ─── IOutboxRepository ─── RabbitMQ
```

### Consolidation API (Cache-First)
```
ConsolidationEndpoints  ←── HTTP (Minimal API)
     ↓
ConsolidationService ─── IConsolidationCache ─── RedisConsolidationCache ─── Redis
                     ↘── IConsolidationRepository ─── MongoConsolidationRepository ─── MongoDB
```

### Consolidation Worker (Event-Driven)
```
RabbitMQ ─── TransactionCreatedConsumer (BackgroundService)
                    ↓
              IdempotencyChecker ─── MongoDB (processed_events)
                    ↓
              ConsolidationCalculator
                    ↓              ↓
         ITransactionReader  IConsolidationRepository
                ↓                     ↓
    MongoTransactionReader  MongoConsolidationRepository
      (transactions_db)       (consolidation_db)
                    ↓
         ICacheInvalidator ─── RedisCacheInvalidator ─── Redis
```

---

**Status:** ✅ Entendimento validado. Pronto para implementação.

---

*Próximas ações após esta sessão:*
1. ✅ Criar 03-component-transactions.md
2. ✅ Criar 04-component-consolidation.md
3. ✅ Criar 06-architectural-patterns.md
4. → Sessão 04: ADRs (Architecture Decision Records)
