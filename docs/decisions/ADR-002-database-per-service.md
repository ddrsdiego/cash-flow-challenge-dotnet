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

- **Transactions** — recebe lançamentos individuais, exige escrita de baixa latência e atomicidade entre a transação e seu respectivo evento de notificação (Outbox Pattern)
- **Consolidation** — mantém um único documento de saldo por dia, atualizado de forma incremental a cada evento recebido; leitura intensiva com cache Redis

Ambos precisam persistir dados, mas têm necessidades radicalmente diferentes. A forma como os bancos são organizados tem impacto direto em:

1. **Acoplamento entre serviços** — qualquer schema compartilhado cria dependências implícitas
2. **Atomicidade do Outbox Pattern** — `transactions` e `outbox` devem compartilhar o mesmo contexto transacional
3. **Isolamento de falhas** — uma falha de conexão com o banco não deve propagar entre serviços
4. **Isolamento do Worker** — o Consolidation Worker não deve ter visibilidade sobre o schema do Transactions Service

### Princípio: Isolamento Como Contrato Arquitetural

O isolamento de banco não é apenas uma preferência operacional — é um **contrato arquitetural** que garante a validade das demais decisões do sistema. Se os bancos forem compartilhados, qualquer modificação de schema em um serviço torna-se um risco para o outro, destruindo a autonomia dos bounded contexts.

```
┌──────────────────────────────────────────────────────────┐
│  PRINCÍPIO: UM SERVIÇO = UM BANCO = UM DOMÍNIO           │
│                                                          │
│  Transactions API ──────────────── transactions_db       │
│  (escrita + Outbox)                 (transactions,       │
│                                      outbox)             │
│                                                          │
│  Consolidation API ─────────────── consolidation_db      │
│  Consolidation Worker               (daily_consolidation,│
│  (leitura + delta incremental)       processed_events)   │
│                                                          │
│  Keycloak ──────────────────────── keycloak_db           │
│  (autenticação OAuth2/OIDC)         (PostgreSQL —        │
│                                      requisito do        │
│                                      próprio Keycloak)   │
└──────────────────────────────────────────────────────────┘
```

### Problema Específico: Como o Worker Acessa Dados de Transações?

Esta é a decisão mais sensível da ADR. Existem duas abordagens possíveis:

**Opção A — Cross-Database Read:** Worker acessa `transactions_db` para buscar todas as transações do dia e recalcula o saldo do zero.

**Opção B — Event-Carried State Transfer:** O evento `TransactionCreated` carrega todos os dados necessários para o Worker (`type`, `amount`, `date`). O Worker aplica um delta incremental no documento de consolidação sem precisar consultar `transactions_db`.

A ADR-001 definiu o Outbox Pattern como mecanismo de garantia de consistência. O evento publicado pelo Outbox carrega dados suficientes para o cálculo. Isso torna a **Opção A desnecessária** e a **Opção B a abordagem natural e correta**.

```
Evento: TransactionCreated {
  type:   "CREDIT",   ← necessário para saber qual campo incrementar
  amount: 500.00,     ← necessário para o delta
  date:   "2024-03-15" ← necessário para localizar o documento de consolidação
}

Worker:
  atual = GetByDateAsync("2024-03-15")  ← lê de consolidation_db
  se CREDIT → atual.totalCredits += 500.00
  balance = totalCredits - totalDebits
  UpsertAsync(atual)                    ← escreve em consolidation_db
```

Não há necessidade de acessar `transactions_db`. O banco do Transactions permanece 100% privado.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Schema change em Transactions não deve afetar Consolidation | Princípio de bounded context, DDD |
| Atomicidade do Outbox: `transactions` e `outbox` no mesmo banco | ADR-001 — Outbox Pattern |
| Worker deve operar exclusivamente em `consolidation_db` | Isolamento de banco como contrato arquitetural |
| Modelo de dados documental (transações como documentos) | Fit natural com MongoDB |
| Transações ACID multi-documento (nativas no MongoDB 4.0+) | Requisito técnico do Outbox Pattern |
| Escalar bancos de forma independente (Transactions vs Consolidation) | RNF 2.2 — Escalabilidade horizontal |

---

## Opções Consideradas

1. **Database-per-Service com MongoDB + Event-Carried State** ← **escolhida**
2. Banco de dados compartilhado (shared schema)
3. Banco de dados compartilhado com schemas separados (namespace isolation)
4. Database-per-Service com PostgreSQL
5. Database-per-Service com MongoDB + Cross-Database Read pelo Worker

---

## Análise Comparativa

### Opção 2: Banco Compartilhado (Shared Schema)

Todas as collections (transactions, outbox, daily_consolidation, processed_events) em uma única instância MongoDB sem separação de contexto.

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ❌ Máximo — qualquer mudança afeta ambos os serviços |
| Isolamento de falhas | ❌ Uma query lenta pode afetar todos os serviços |
| Escala independente | ❌ Impossível — banco único |
| Autonomia de bounded context | ❌ Destruída |
| Complexidade operacional | ✅ Baixa |
| **Veredicto** | **Descartado — viola o princípio de bounded context** |

---

### Opção 3: Banco Compartilhado com Namespace Isolation

Prefixo de collection por serviço: `transactions.*` e `consolidation.*` na mesma instância MongoDB.

| Critério | Avaliação |
|----------|-----------|
| Acoplamento de schema | ⚠️ Reduzido, mas ainda existe acesso cruzado possível |
| Isolamento de falhas | ❌ Compartilha connection pool e locks MongoDB |
| Atomicidade do Outbox | ⚠️ Tecnicamente possível, mas cria confusão de responsabilidade |
| Escala independente | ❌ Limitado — mesma instância MongoDB |
| Autonomia de bounded context | ⚠️ Parcial |
| **Veredicto** | **Descartado — isolamento incompleto** |

**Motivo do descarte:** A separação de namespace não garante isolamento real. O banco continua compartilhando recursos (threads, memória, locks). Um índice malformado em uma collection afeta o throughput de toda a instância.

---

### Opção 4: Database-per-Service com PostgreSQL

Cada serviço com seu próprio banco PostgreSQL relacional.

| Critério | Transactions Service | Consolidation Service |
|----------|---------------------|-----------------------|
| Modelo de dados | ⚠️ Tabela de transações com JOIN para outbox | ⚠️ Tabela daily_consolidation |
| ACID transactions | ✅ Nativo e robusto | ✅ Nativo e robusto |
| Outbox Pattern | ✅ Tabela outbox na mesma transação | — |
| Schema flexível | ❌ DDL migration para cada mudança | ❌ DDL migration |
| Fit documental | ❌ Impedância objeto-relacional | ❌ Impedância objeto-relacional |
| Stack homogênea | ❌ Adiciona PostgreSQL ao stack | ❌ Adiciona PostgreSQL |
| Escalabilidade horizontal | ⚠️ Sharding complexo | ⚠️ Sharding complexo |
| **Veredicto** | **Descartado** | **Descartado** |

**Motivo do descarte:** O modelo de dados do sistema (transações financeiras como documentos, consolidado como documento único por dia) é um fit natural para bancos documentais. Adicionar PostgreSQL ao stack introduz duas tecnologias diferentes sem ganho real — as transações ACID necessárias para o Outbox são suportadas nativamente pelo MongoDB desde a versão 4.0 com multi-document transactions. A homogeneidade de stack (MongoDB em todos os serviços de aplicação) simplifica operação, monitoramento e onboarding.

---

### Opção 5: Database-per-Service com MongoDB + Cross-Database Read

Bancos separados por serviço, mas o Consolidation Worker lê de `transactions_db` para calcular o saldo.

| Critério | Avaliação |
|----------|-----------|
| Isolamento de schema | ❌ Worker tem dependência implícita do schema de Transactions |
| Autonomia do Transactions Service | ❌ Qualquer mudança no schema de `transactions` pode quebrar o Worker |
| Necessidade real | ❌ Desnecessário — evento já carrega `type + amount + date` |
| Consistência eventual | ⚠️ Worker precisaria de mecanismo de snapshot para recálculo completo |
| Complexidade adicional | ❌ Conexão extra, usuário read-only, regras de firewall entre databases |
| **Veredicto** | **Descartado — acoplamento implícito desnecessário** |

**Motivo do descarte:** A ADR-001 define que o evento `TransactionCreated` (publicado via Outbox) carrega os dados `type`, `amount` e `date`. Esses dados são suficientes para o Worker aplicar um delta incremental no documento de consolidação. Acessar `transactions_db` não adiciona nenhuma capacidade nova — apenas cria uma dependência de schema que destrói a autonomia do bounded context de Transactions. O Worker que acessa `transactions_db` também seria vulnerável a qualquer problema de performance ou disponibilidade desse banco, criando acoplamento operacional além do acoplamento de schema.

---

### Análise Comparativa: MongoDB vs PostgreSQL para Serviços de Aplicação

| Critério | MongoDB | PostgreSQL |
|----------|---------|------------|
| Modelo de dados | ✅ Documental — fit natural para transações e consolidações | ⚠️ Relacional — impedância objeto-relacional |
| Transações ACID multi-documento | ✅ Nativas desde v4.0 (necessário para Outbox) | ✅ Nativas |
| Schema flexível | ✅ Schema-less — iteração rápida | ❌ DDL migration obrigatória |
| Escalabilidade horizontal | ✅ Sharding nativo | ⚠️ Sharding complexo (Citus, etc.) |
| Driver .NET | ✅ MongoDB.Driver oficial | ✅ Npgsql oficial |
| Operação Docker Compose | ✅ Single container | ✅ Single container |
| Homogeneidade de stack | ✅ Uma tecnologia para todos os serviços app | ❌ Adiciona segunda tecnologia |
| Índice único em campo | ✅ Simples | ✅ Simples |
| Tipo decimal financeiro | ✅ Decimal128 | ✅ numeric(18,2) |
| Experiência do time | Avaliação contextual | Avaliação contextual |

**Nota sobre Keycloak:** O Keycloak exige PostgreSQL como seu banco padrão. Essa decisão é do próprio Keycloak, não uma escolha da aplicação. O `keycloak_db` é operado pelo Keycloak de forma autônoma e não faz parte do escopo de decisão desta ADR.

---

## Decisão

**Adotar Database-per-Service com MongoDB para todos os serviços de aplicação, com Event-Carried State Transfer como mecanismo de comunicação de dados entre bounded contexts.**

### Mapeamento de Bancos

| Serviço | Banco | Tecnologia | Collections |
|---------|-------|-----------|-------------|
| Transactions API + OutboxPublisher | `transactions_db` | MongoDB | `transactions`, `outbox` |
| Consolidation API | `consolidation_db` | MongoDB | `daily_consolidation` |
| Consolidation Worker | `consolidation_db` | MongoDB | `daily_consolidation`, `processed_events` |
| Keycloak | `keycloak_db` | PostgreSQL | (gerenciado pelo Keycloak) |

### Regra de Isolamento

> **Cada serviço lê e escreve exclusivamente em seu próprio banco. Nenhum serviço acessa o banco de outro serviço, seja para leitura ou escrita.**

```
Transactions API    ──► transactions_db    (leitura + escrita)
OutboxPublisher     ──► transactions_db    (leitura + escrita do outbox)

Consolidation API   ──► consolidation_db   (leitura)
Consolidation Worker──► consolidation_db   (leitura + escrita)

Keycloak            ──► keycloak_db        (gerenciado pelo Keycloak)

❌ PROIBIDO: Consolidation Worker → transactions_db
❌ PROIBIDO: Transactions API → consolidation_db
```

### Garantia de Atomicidade no Outbox Pattern

A escolha de MongoDB para `transactions_db` é motivada também pela necessidade do Outbox Pattern (ADR-001). O Outbox exige que `INSERT transactions` e `INSERT outbox` sejam atômicos. Com MongoDB multi-document transactions:

```
BEGIN session
  db.transactions.insertOne(transaction)    ← mesma transação
  db.outbox.insertOne(outboxEvent)          ← mesma transação
COMMIT                                      ← ambos ou nenhum
```

PostgreSQL também suportaria essa atomicidade, mas introduziria uma segunda tecnologia no stack sem benefício adicional para este caso de uso.

### Event-Carried State Transfer

O evento `TransactionCreated` é projetado para carregar todos os dados que o Consolidation Worker necessita:

```json
{
  "eventType": "TransactionCreated",
  "idempotencyKey": "{{uuid}}",
  "data": {
    "type":   "CREDIT",
    "amount":  500.00,
    "date":   "2024-03-15"
  }
}
```

O Worker aplica um **delta incremental** ao documento de consolidação existente:

```
ATUAL (consolidation_db.daily_consolidation WHERE date = "2024-03-15"):
  { totalCredits: 300, totalDebits: 150, balance: 150, count: 2 }

DELTA DO EVENTO:
  type = CREDIT → totalCredits += 500

RESULTADO:
  { totalCredits: 800, totalDebits: 150, balance: 650, count: 3 }
```

Esse padrão elimina completamente a necessidade de leitura cross-database e é **idempotente**: o mesmo evento processado duas vezes produz o mesmo resultado (garantido pelo `idempotencyKey` + `processed_events`).

---

## Consequências

### Positivas ✅

- **Autonomia total de schema:** O Transactions Service pode adicionar, remover ou renomear campos sem qualquer risco para o Consolidation Service.
- **Isolamento de falhas de banco:** Uma lentidão ou indisponibilidade em `transactions_db` não afeta as leituras da Consolidation API.
- **Worker completamente isolado:** O Consolidation Worker nunca precisa de credenciais ou conectividade com `transactions_db`.
- **Atomicidade do Outbox garantida:** MongoDB multi-document transactions asseguram que `transactions` e `outbox` são sempre consistentes.
- **Stack homogênea:** MongoDB em todos os serviços de aplicação — uma tecnologia, um driver, um modelo mental.
- **Escalabilidade independente:** `transactions_db` pode ser escalado com réplicas de leitura sem afetar `consolidation_db`.

### Negativas — Trade-offs Aceitos ⚠️

- **Sem JOIN entre bancos:** Consultas que cruzam dados de Transactions e Consolidation não são possíveis via query. Exigem processamento no nível de aplicação ou eventos.
- **Consistência eventual:** O saldo consolidado reflete o estado dos lançamentos com lag (normalmente < 500ms). Um lançamento acabado de criar pode não aparecer imediatamente no consolidado.
- **Contrato do evento é crítico:** O evento `TransactionCreated` deve sempre incluir `type`, `amount` e `date`. Mudanças nessa estrutura requerem versionamento (ex: `"version": "2.0"`).
- **Delta incremental sem snapshot:** Se um lançamento precisar ser corrigido ou cancelado no futuro, será necessário um evento compensatório (ex: `TransactionCancelled`) — não é possível simplesmente "recalcular tudo" sem reprocessar o histórico de eventos.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Evento `TransactionCreated` muda sem versionamento | Baixa | Alto (Worker quebra silenciosamente) | Versão obrigatória no contrato do evento (`"version": "1.0"`); consumer deve verificar versão |
| Delta incremental diverge do estado real (bug) | Baixa | Alto (saldo errado) | Teste de regressão comparando recálculo completo com estado incremental; script de reconciliação |
| `consolidation_db` indisponível | Média (MVP: single node) | Alto (Worker não processa, API não lê) | DLQ preserva eventos; em produção usar MongoDB replica set |
| Crescimento não monitorado de `processed_events` | Baixa | Médio (storage) | Índice TTL de 7 dias já configurado — limpeza automática |

### ADRs que Dependem desta Decisão

- **[ADR-001](ADR-001-async-communication.md):** O Outbox Pattern requer que `transactions` e `outbox` estejam no mesmo banco transacional — satisfeito pela escolha de MongoDB com database-per-service.
- **[ADR-003](ADR-003-user-context-propagation.md):** O `userId` armazenado na collection `transactions` é extraído do JWT e nunca vem de outro banco — reforça o princípio de isolamento: dados de identidade vêm do token, não de consultas cross-service.

---

## Referências

- [Database per Service — microservices.io](https://microservices.io/patterns/data/database-per-service.html)
- [Event-Carried State Transfer — Martin Fowler](https://martinfowler.com/articles/201701-event-driven.html)
- [MongoDB Multi-Document Transactions](https://www.mongodb.com/docs/manual/core/transactions/)
- [Bounded Context — DDD Reference](https://www.domainlanguage.com/ddd/reference/)
- ADR-001 (Outbox Pattern): `docs/decisions/ADR-001-async-communication.md`
- Padrão detalhado: `docs/architecture/06-architectural-patterns.md` — Seção 6 (Database-per-Service)
- Componentes do Worker: `docs/architecture/04-component-consolidation.md` — ConsolidationCalculator
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Seções 1.4, 2.2
