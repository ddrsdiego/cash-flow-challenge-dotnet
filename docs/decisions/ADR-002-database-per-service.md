# ADR-002: Database-per-Service com MongoDB

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-002 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-26 (simplificação de formato — foco em decisão) |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **Supersedes** | Decisão implícita de stack em `docs/plano-implementacao.md` |
| **ADRs Relacionadas** | [ADR-001](ADR-001-async-communication.md), [ADR-003](ADR-003-user-context-propagation.md) |

---

## Contexto e Problema

O sistema é composto por dois bounded contexts com modelos de dados completamente distintos:

- **Transactions** — recebe lançamentos individuais, exige escrita de baixa latência e atomicidade entre persistência e notificação (Outbox Pattern)
- **Consolidation** — mantém saldo consolidado por dia, atualizado de forma incremental a cada evento; padrão de acesso predominantemente de leitura

### Forças em Tensão

- **Autonomia de schema** pressiona para Database-per-Service — mudança de schema em um serviço não pode afetar o outro
- **Requisito de atomicidade do Outbox** (ADR-001) pressiona para banco isolado — lançamento e evento devem confirmar juntos na mesma transação
- **Isolamento de falhas** pressiona para banco isolado — problema em um banco não degradará consultas do outro
- **Simplicidade operacional** pressiona para banco único — fewer moving parts
- **Modelo de dados documental** pressiona para MongoDB — lançamentos financeiros são naturalmente documentos independentes
- **Suporte a transações ACID** pressiona para MongoDB — requisito crítico do Outbox Pattern
- **Escalabilidade independente** pressiona para banco isolado — cada serviço escala conforme sua demanda específica

### Problema Central

Dois serviços com modelos de dados distintos precisam colaborar via eventos sem criar acoplamento de schema ou falhas em cascata. Como garantir atomicidade entre persistência e notificação mantendo isolamento de falhas?

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Schema change em Transactions não deve afetar Consolidation | Princípio de bounded context — DDD |
| Atomicidade do Outbox: lançamento e evento na mesma transação | ADR-001 — Outbox Pattern |
| Worker deve operar exclusivamente no banco de Consolidation | Isolamento de banco como contrato arquitetural |
| Modelo de dados documental é fit natural para o domínio | Transações financeiras como documentos independentes |
| Suporte a transações ACID multi-documento | Requisito técnico do Outbox Pattern |
| Escalabilidade independente dos bancos de dados | RNF — Escalabilidade horizontal |

---

## Opções Consideradas

1. **Database-per-Service com MongoDB + Event-Carried State** ← **escolhida**
2. Banco de dados compartilhado (shared schema)
3. Banco de dados compartilhado com separação por namespace
4. Database-per-Service com PostgreSQL
5. Database-per-Service com MongoDB + acesso direto ao banco do Transactions

---

## Análise Comparativa

### Opção 2: Banco Compartilhado

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ❌ Máximo — qualquer mudança afeta todos os serviços |
| Isolamento de falhas | ❌ Uma query lenta degrada todos os serviços |
| Autonomia de bounded context | ❌ Destruída |
| **Veredicto** | **Descartado — viola o princípio de bounded context** |

---

### Opção 3: Banco Compartilhado com Namespace

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ⚠️ Reduzido, mas acesso cruzado ainda é possível |
| Isolamento de falhas | ❌ Compartilha recursos — memória, threads, locks |
| Escalabilidade independente | ❌ Impossível |
| **Veredicto** | **Descartado — isolamento incompleto** |

---

### Opção 4: Database-per-Service com PostgreSQL

| Critério | MongoDB | PostgreSQL |
|----------|---------|------------|
| Modelo de dados | ✅ Documental — fit natural | ⚠️ Relacional — impedância |
| Transações ACID multi-documento | ✅ Nativas v4.0+ | ✅ Nativas |
| Schema flexível | ✅ Sem migrações DDL | ❌ Migrações obrigatórias |
| Escalabilidade horizontal | ✅ Sharding nativo | ⚠️ Complexo |
| **Veredicto** | **Escolhido** | **Descartado — modelo relacional não oferece ganho funcional para o domínio** |

---

### Opção 5: Database-per-Service com MongoDB + Acesso Direto

| Critério | Avaliação |
|----------|-----------|
| Isolamento de schema | ❌ Worker tem dependência do schema de Transactions |
| Autonomia de Transactions | ❌ Qualquer mudança pode quebrar o Worker |
| Necessidade real | ❌ Evento transporta dados suficientes |
| **Veredicto** | **Descartado — acoplamento implícito injustificado** |

---

## Decisão

**Adotamos Database-per-Service com MongoDB para todos os serviços de aplicação, com Event-Carried State Transfer como mecanismo de propagação de dados entre bounded contexts.**

### Mapeamento de Bancos

| Serviço | Banco | Responsabilidade |
|---------|-------|-----------------|
| Transactions API | `transactions_db` | Leitura e escrita de lançamentos; registro de eventos pendentes (Outbox) |
| Consolidation API | `consolidation_db` | Leitura do saldo consolidado |
| Consolidation Worker | `consolidation_db` | Atualização incremental do saldo |

### Regra de Isolamento

> **Cada serviço lê e escreve exclusivamente em seu próprio banco. Nenhum serviço acessa o banco de outro serviço, seja para leitura ou escrita.**

### Event-Carried State Transfer

O evento publicado pelo Outbox carrega todos os dados necessários ao Consolidation Worker para aplicar um delta incremental ao saldo — sem jamais consultar o banco de Transactions.

> Para detalhes do contrato do evento e pipeline de processamento assíncrono, consulte: `docs/architecture/07-async-pipeline-details.md`

---

## Consequências

### Positivas ✅

- **Autonomia total de schema:** Transactions pode evoluir seu modelo sem afetar Consolidation
- **Isolamento de falhas:** Problemas em `transactions_db` não afetam as leituras da Consolidation API
- **Atomicidade garantida:** Transações multi-documento do MongoDB asseguram consistência entre lançamento e evento
- **Stack homogênea:** MongoDB em todos os serviços de aplicação — uma única tecnologia, um único modelo mental operacional
- **Escalabilidade independente:** Cada banco escala conforme a demanda específica

### Negativas — Trade-offs Aceitos ⚠️

- **Sem JOIN entre bancos:** Consultas que cruzam Transactions e Consolidation não são possíveis via query — exigem composição no nível de aplicação
- **Consistência eventual:** Saldo consolidado reflete lançamentos com lag (P95 < 5s, melhor caso <500ms)
- **Contrato do evento é crítico:** O evento deve transportar dados suficientes; mudanças de contrato exigem versionamento explícito

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Contrato do evento muda sem versionamento | Baixa | Alto | Versão obrigatória no contrato; consumer verifica compatibilidade |
| Divergência entre saldo incremental e estado real | Baixa | Alto | Testes de regressão; script de reconciliação periódica |
| `consolidation_db` indisponível (MVP single-node) | Média | Alto | DLQ preserva eventos; replica set de 3 nós em produção |

---

## Referências

- **Padrões de Microserviços:** https://microservices.io/patterns/data/database-per-service.html
- **Event-Carried State Transfer:** https://martinfowler.com/articles/201701-event-driven.html
- **DDD Bounded Context:** https://www.domainlanguage.com/ddd/reference/
- ADR-001: `docs/decisions/ADR-001-async-communication.md` — Outbox Pattern
- ADR-003: `docs/decisions/ADR-003-user-context-propagation.md` — Propagação de identidade
- **Especificação Técnica Detalhada:** `docs/architecture/06-architectural-patterns.md` (Seção 7 — MongoDB Collections) — schema, índices, ciclos de vida
- **Configuração de Infraestrutura:** `docs/operations/01-deployment-strategy.md` — replica set, deployment
- **Segurança de Dados:** `docs/security/04-data-protection.md` — criptografia em repouso, chaves

---

## 📜 Histórico de Revisões

### Revisão 1 (2026-03-19)
Decisão inicial de Database-per-Service com MongoDB e Event-Carried State Transfer como mecanismo de propagação de dados.

### Revisão 2 (2026-03-25)
Detalhamento de coleções, ciclos de vida (RawRequest, ReceivedTransaction), Outbox via MassTransit, especificações técnicas de infraestrutura (replica set, segurança, idempotência).

### Revisão 3 (2026-03-26)
Simplificação de formato para conformidade com padrão ADR de Martin Fowler. Conteúdo técnico detalhado movido para documentos especializados (`06-architectural-patterns.md`, `07-async-pipeline-details.md`, `docs/operations/`, `docs/security/`). ADR refocada em decisão e consequências (redução de ~500 para ~200 linhas).

### Revisão 4 (2026-03-26)
Ajustes de conformidade com padrão Fowler: (1) reformulação da seção "Contexto e Problema" para estrutura explícita de forças em tensão + problema central, (2) correção de voz ativa na Decisão (infinitivo → primeira pessoa), (3) padronização de formato do Veredicto da Opção 4, (4) adição de referência técnica para Event-Carried State Transfer Transfer.
