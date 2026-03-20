# ADR-002: Database-per-Service com MongoDB

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-002 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-001](ADR-001-async-communication.md), [ADR-003](ADR-003-user-context-propagation.md) |

---

## Contexto e Problema

O sistema é composto por dois bounded contexts com modelos de dados, padrões de acesso e ciclos de vida completamente distintos:

- **Transactions** — recebe lançamentos individuais, exige escrita de baixa latência e atomicidade entre o lançamento e seu evento de notificação (Outbox Pattern)
- **Consolidation** — mantém um saldo consolidado por dia, atualizado de forma incremental a cada evento recebido; padrão de acesso predominantemente de leitura, com cache intermediário

A forma como os dados são organizados tem impacto direto em:

1. **Acoplamento entre serviços** — schema compartilhado cria dependências implícitas que comprometem a autonomia dos bounded contexts
2. **Atomicidade do Outbox Pattern** — os dados do lançamento e o registro do evento a ser publicado devem compartilhar o mesmo contexto transacional
3. **Isolamento de falhas** — uma falha de banco em um serviço não deve propagar para o outro

### Princípio: Isolamento como Contrato Arquitetural

O isolamento de banco não é apenas uma preferência operacional — é um **contrato arquitetural** que garante a validade das demais decisões do sistema. Um banco compartilhado torna qualquer modificação de schema em um serviço um risco para todos os outros, destruindo a autonomia dos bounded contexts.

### Problema Central: Acesso a Dados entre Serviços

Como o Consolidation Worker obtém as informações necessárias para calcular o saldo sem acessar o banco do Transactions Service?

**Opção A — Acesso direto ao banco:** Worker consulta o banco do Transactions Service para buscar os lançamentos do dia e recalcula o saldo.

**Opção B — Event-Carried State Transfer:** O evento publicado pelo Outbox carrega todos os dados necessários ao Worker (`tipo`, `valor`, `data`). O Worker aplica um delta incremental no consolidado sem nunca consultar o banco do Transactions Service.

A ADR-001 define o Outbox Pattern como mecanismo de consistência entre os serviços. O evento publicado transporta dados suficientes para o cálculo. A Opção A é desnecessária e cria acoplamento implícito; a Opção B é a abordagem natural e correta.

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

Todos os serviços acessam o mesmo banco de dados e as mesmas collections.

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ❌ Máximo — qualquer mudança afeta todos os serviços |
| Isolamento de falhas | ❌ Uma query lenta pode degradar todos os serviços |
| Autonomia de bounded context | ❌ Destruída |
| **Veredicto** | **Descartado — viola o princípio de bounded context** |

---

### Opção 3: Banco Compartilhado com Separação por Namespace

Prefixo por serviço dentro do mesmo banco. O isolamento é lógico, não físico.

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ⚠️ Reduzido, mas acesso cruzado ainda é possível |
| Isolamento de falhas | ❌ Compartilha recursos — memória, threads, locks |
| Escalabilidade independente | ❌ Impossível — mesma instância |
| **Veredicto** | **Descartado — isolamento incompleto** |

---

### Opção 4: Database-per-Service com PostgreSQL

Cada serviço com seu próprio banco relacional.

| Critério | MongoDB | PostgreSQL |
|----------|---------|------------|
| Modelo de dados | ✅ Documental — fit natural para lançamentos e consolidações | ⚠️ Relacional — impedância objeto-relacional |
| Transações ACID multi-documento | ✅ Nativas desde v4.0 (requisito do Outbox) | ✅ Nativas |
| Schema flexível | ✅ Iteração sem migrações DDL | ❌ DDL migration obrigatória a cada mudança |
| Escalabilidade horizontal | ✅ Sharding nativo | ⚠️ Sharding complexo |
| Homogeneidade de stack | ✅ Uma tecnologia para todos os serviços de aplicação | ❌ Adiciona segunda tecnologia ao stack |

**PostgreSQL descartado:** O modelo de dados do sistema — lançamentos como documentos independentes, saldo como documento único por data — é um fit natural para bancos documentais. As transações ACID necessárias para o Outbox Pattern são suportadas nativamente pelo MongoDB desde a versão 4.0. Adicionar PostgreSQL ao stack introduz heterogeneidade sem ganho funcional.

> **Nota:** O Keycloak exige PostgreSQL como seu banco padrão. Essa é uma decisão do próprio Keycloak, não da aplicação. O banco de identidade opera de forma completamente isolada dos bancos de aplicação.

---

### Opção 5: Database-per-Service com MongoDB + Acesso Direto ao Banco do Transactions

Bancos separados por serviço, mas o Consolidation Worker lê diretamente do banco do Transactions Service.

| Critério | Avaliação |
|----------|-----------|
| Isolamento de schema | ❌ Worker tem dependência implícita do schema de Transactions |
| Autonomia do Transactions Service | ❌ Qualquer mudança no schema pode quebrar o Worker |
| Necessidade real | ❌ Desnecessário — o evento transporta os dados suficientes para o cálculo |
| Isolamento de falhas | ❌ Problema em `transactions_db` pode impactar o Worker |
| **Veredicto** | **Descartado — acoplamento implícito sem justificativa** |

---

## Decisão

**Adotar Database-per-Service com MongoDB para todos os serviços de aplicação, com Event-Carried State Transfer como mecanismo de propagação de dados entre bounded contexts.**

### Mapeamento de Bancos

| Serviço | Banco | Responsabilidade |
|---------|-------|-----------------|
| Transactions API | `transactions_db` | Leitura e escrita de lançamentos; registro de eventos pendentes (Outbox) |
| Consolidation API | `consolidation_db` | Leitura do saldo consolidado |
| Consolidation Worker | `consolidation_db` | Atualização do saldo consolidado; registro de idempotência |
| Keycloak | `keycloak_db` (PostgreSQL) | Gerenciado pelo próprio Keycloak |

### Regra de Isolamento

> **Cada serviço lê e escreve exclusivamente em seu próprio banco. Nenhum serviço acessa o banco de outro serviço, seja para leitura ou escrita.**

```
Transactions API    ──► transactions_db    (leitura + escrita)
Outbox Publisher    ──► transactions_db    (leitura + atualização de eventos)

Consolidation API   ──► consolidation_db   (leitura)
Consolidation Worker──► consolidation_db   (leitura + escrita)

❌ PROIBIDO: Consolidation Worker → transactions_db
❌ PROIBIDO: Transactions API → consolidation_db
```

### Event-Carried State Transfer

O evento publicado pelo Outbox carrega todos os dados que o Consolidation Worker necessita para aplicar um delta incremental ao saldo consolidado — sem jamais precisar consultar o banco do Transactions Service.

Esse padrão garante:
- ✅ Isolamento completo entre bounded contexts
- ✅ Autonomia total de schema do Transactions Service
- ✅ Worker operando exclusivamente sobre seu próprio banco
- ✅ Operação idempotente — mesmo evento processado múltiplas vezes não corrompe o estado

---

## Consequências

### Positivas ✅

- **Autonomia total de schema:** O Transactions Service pode evoluir seu modelo de dados sem nenhum risco para o Consolidation Service.
- **Isolamento de falhas de banco:** Uma lentidão ou indisponibilidade em `transactions_db` não afeta as leituras da Consolidation API.
- **Worker completamente isolado:** O Consolidation Worker nunca precisa de credenciais ou conectividade com `transactions_db`.
- **Atomicidade do Outbox garantida:** As transações multi-documento do MongoDB asseguram que lançamento e evento de notificação são sempre consistentes.
- **Stack homogênea:** MongoDB em todos os serviços de aplicação — uma única tecnologia de banco, um único modelo mental operacional.
- **Escalabilidade independente:** Cada banco pode ser escalado conforme a demanda específica do seu serviço.

### Negativas — Trade-offs Aceitos ⚠️

- **Sem JOIN entre bancos:** Consultas que cruzam dados de Transactions e Consolidation não são possíveis via query — exigem composição no nível de aplicação.
- **Consistência eventual:** O saldo consolidado reflete os lançamentos com lag (normalmente abaixo de 500ms).
- **Contrato do evento é crítico:** O evento publicado pelo Outbox deve sempre transportar os dados necessários ao Worker. Mudanças no contrato exigem versionamento explícito.
- **Delta incremental sem snapshot:** Se um lançamento precisar ser cancelado no futuro, será necessário um evento compensatório — não é possível simplesmente recalcular sem reprocessar o histórico.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Contrato do evento muda sem versionamento | Baixa | Alto | Versão obrigatória no contrato do evento; consumer deve verificar compatibilidade |
| Divergência entre saldo incremental e estado real (bug) | Baixa | Alto | Testes de regressão; script de reconciliação periódica |
| `consolidation_db` indisponível (instância única no MVP) | Média | Alto | DLQ preserva eventos; replica set em produção |
| Crescimento de dados de idempotência sem limpeza automática | Baixa | Médio | Política de expiração automática (TTL) sobre registros de idempotência |

---

## Referências

- [Database per Service — microservices.io](https://microservices.io/patterns/data/database-per-service.html)
- [Event-Carried State Transfer — Martin Fowler](https://martinfowler.com/articles/201701-event-driven.html)
- [Bounded Context — DDD Reference](https://www.domainlanguage.com/ddd/reference/)
- ADR-001 (Outbox Pattern): `docs/decisions/ADR-001-async-communication.md`
- Padrões arquiteturais: `docs/architecture/06-architectural-patterns.md`
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md`
