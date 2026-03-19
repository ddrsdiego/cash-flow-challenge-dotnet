# Entendimento da Tarefa — Sessão 05

## Referência
Sessão anterior: `.arquitetura/sessao-04/entendimento.md`
Plano: `docs/plano-implementacao.md` (FASE 1, item 1.6)

---

## Contexto

Continuação da Sessão 04, que produziu a ADR-001.

**Estado do projeto:**
- ✅ **Sessão 01** — `01-functional-requirements.md`, `02-non-functional-requirements.md`, `05-domain-mapping.md`
- ✅ **Sessão 02** — `01-context-diagram.md` (C4 Level 1), `02-container-diagram.md` (C4 Level 2)
- ✅ **Sessão 03** — `03-component-transactions.md`, `04-component-consolidation.md`, `06-architectural-patterns.md`
- ✅ **Sessão 04** — `ADR-001-async-communication.md`
- ✅ **Sessão 05** — `ADR-002-database-per-service.md` + correções nos documentos existentes (esta sessão)

---

## Tarefa

Produzir `ADR-002-database-per-service.md` — o segundo Architecture Decision Record da série.

---

## Atividades Realizadas

### Fase 1 — Análise Crítica de Inconsistências

Antes de produzir a ADR-002, foram identificadas e corrigidas inconsistências em três documentos existentes. Todos os documentos anteriores continham a afirmação incorreta de que o **Consolidation Worker leria de `transactions_db`** (cross-database read). Isso contradiz a decisão de Outbox Pattern da ADR-001, onde o evento `TransactionCreated` já carrega todos os dados necessários (`type`, `amount`, `date`).

| Documento | Problema Identificado | Correção Aplicada |
|-----------|----------------------|-------------------|
| `05-domain-mapping.md` | Worker flow: "Busca transações para 2024-03-15" | Substituído por: "Aplica delta do evento (CREDIT → totalCredits += amount)" |
| `06-architectural-patterns.md` | Seção 6: `Consolidation Worker──→ transactions_db (read-only)` | Removido. Worker só acessa `consolidation_db`. Nota de "trade-off consciente" corrigida para explicar o Event-Carried State Transfer. |
| `04-component-consolidation.md` | Componentes `ITransactionReader`, `MongoTransactionReader`, `txmongo` existiam. Diagrama C4 e sequence diagram mostravam cross-DB read. | Removidos componentes inexistentes. `ConsolidationCalculator` reescrito para usar delta incremental. Diagrama Mermaid, ciclo do Consumer e sequence diagram corrigidos. Tabela de padrões atualizada: "Cross-database read" → "Event-Carried State Transfer" + "Delta Incremental". |

### Fase 2 — Produção da ADR-002

Com os documentos base corrigidos, a ADR-002 foi produzida documentando formalmente a decisão.

---

## Objetivo

Documentar formalmente **por que** a arquitetura Database-per-Service com MongoDB foi escolhida, incluindo:
- ✅ 5 opções analisadas com tabelas comparativas
- ✅ Decisão crítica sobre isolamento do Worker (Event-Carried State Transfer vs Cross-DB Read)
- ✅ Regra de isolamento explícita (proibições documentadas)
- ✅ Trade-offs aceitos com consciência
- ✅ Riscos com mitigações

---

## Decisão Documentada

**Database-per-Service com MongoDB + Event-Carried State Transfer.**

| Serviço | Banco | Collections |
|---------|-------|-------------|
| Transactions API + OutboxPublisher | `transactions_db` | `transactions`, `outbox` |
| Consolidation API + Worker | `consolidation_db` | `daily_consolidation`, `processed_events` |
| Keycloak | `keycloak_db` (PostgreSQL) | (gerenciado pelo Keycloak) |

**Regra de ouro:** Nenhum serviço acessa o banco de outro serviço — nem para leitura.

---

## Escopo

### IN SCOPE ✅
- Correção de inconsistências nos documentos `04`, `05` e `06`
- ADR-002 completa no formato MADR
- Registro desta sessão

### OUT OF SCOPE ❌
- ADR-003 a ADR-006 (próximas sessões)
- Código-fonte (Sessão 09+)

---

**Status:** ✅ Entendimento validado. Implementação concluída.

---

*Próximas ações após esta sessão:*
1. ✅ Corrigir `05-domain-mapping.md`
2. ✅ Corrigir `06-architectural-patterns.md`
3. ✅ Corrigir `04-component-consolidation.md`
4. ✅ Criar `ADR-002-database-per-service.md`
5. → Sessão seguinte: `ADR-003-cqrs-consolidation.md`
6. → Sessão seguinte: `ADR-004-api-gateway.md`
7. → Sessão seguinte: `ADR-005-authentication-strategy.md`
8. → Sessão seguinte: `ADR-006-observability-stack.md`
