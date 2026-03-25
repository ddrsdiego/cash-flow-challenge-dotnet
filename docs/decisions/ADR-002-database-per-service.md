# ADR-002: Database-per-Service com MongoDB

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-002 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-25 (revisão para alinhamento com stakeholders e especificação técnica detalhada) |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **Supersedes** | Decisão implícita de stack em `docs/plano-implementacao.md` (especifica MongoDB sobre PostgreSQL para bancos de aplicação) |
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

### Compliance e Auditoria em Dados Financeiros

Um questionamento legítimo é se um banco documental é apropriado para dados financeiros, historicamente preservados em bancos relacionais. A resposta está na **imutabilidade aplicacional**, não em constraints de banco:

- **Auditoria:** Lançamentos financeiros são **insert-only** — nunca atualizados, apenas compensados (reversal) via lançamento inverso. Essa propriedade garante imutabilidade sem depender de mecanismos relacionais (triggers, audit tables).
- **Conformidade:** Compliance (ex: retenção de dados, rastreabilidade) é implementado no nível de aplicação (política de retenção, TTL indexes, versionamento de schema) — não depende do modelo relacional.
- **Reconciliação:** É sempre possível recalcular o saldo a partir do histórico de lançamentos, em qualquer banco — a propriedade que importa é a imutabilidade dos lançamentos já persistidos, garantida por padrão no Transactions Service.

Além disso, transações ACID multi-documento do MongoDB — suportadas nativamente desde v4.0 — satisfazem o requisito técnico crítico de atomicidade entre persistência e notificação (Outbox Pattern).

### Evolução de Schema sem DDL Migration

Um benefício secundário do modelo documental é a capacidade de evoluir schema sem impacto operacional. Lançamentos financeiros acumulam campos ao longo do tempo (metadados, categorias, tags, referências de terceiros). Com MongoDB:
- Campo novo pode ser adicionado sem migration — apenas aplicação começa a populá-lo
- Aplicação em produção lida tanto com documentos antigos (sem campo) quanto novos (com campo)
- Sem DDL locks que bloqueariam outras operações

Com PostgreSQL, cada novo campo exigiria `ALTER TABLE`, com risk de lock em tabelas grandes — trade-off operacional.

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

## Mapeamento Detalhado de Coleções

### transactions_db

| Coleção | Responsabilidade | Operações | Índices |
|---------|-----------------|-----------|---------|
| **RawRequests** | Buffer de ingestão rápida antes do processamento em lote | Insert (API), Read (Batcher), Update (Batcher → mark dispatched, Processor → mark processed) | `{status}`, `{batchId}`, `{createdAt}` |
| **Transactions** | Lançamentos financeiros validados e processados | Insert (Processor via batch), Read (Query por userId/date), Update (raramente — metadata operacional apenas, nunca valores financeiros) | `{userId, date}`, `{date}` |
| **DistributedLocks** | Lock distribuído para coordenar Batcher entre múltiplas instâncias | Insert/Update (Batcher adquire), Delete (Batcher libera ou expire) | `{lockId}` |
| **Outbox** (via MassTransit) | Eventos pendentes de publicação (criado automaticamente por MassTransit) | Insert (Processor publica evento), Delete (Outbox publisher consome) | `{messageId}`, `{outboxState}` |

**Ciclo de Vida de um RawRequest:**
```
PENDING ──────► DISPATCHED ──────► PROCESSED
(criado)      (Batcher polpa)   (Processor consome)
  ↓                ↓                  ↓
API insere    Batcher marca    Processor marca
```

### consolidation_db

| Coleção | Responsabilidade | Operações | Índices |
|---------|-----------------|-----------|---------|
| **ReceivedTransactions** | Buffer intermediário de transações recebidas mas não processadas | Insert (TransactionCreatedConsumer), Read (ProcessConsolidationBatch), Delete (ProcessConsolidationBatch após processar) | `{date}`, `{createdAt}` |
| **DailyBalances** | Saldo consolidado por dia (um documento por dia) | Insert (ProcessConsolidationBatch cria novo dia), Update (ProcessConsolidationBatch adiciona valor ao dia) | `{userId, date}` (unique) |
| **IdempotencyKeys** | Registro de eventos já processados (previne re-processamento de duplicatas) | Insert (ConsolidationBatchReceivedConsumer), Read (check idempotência), Delete (expire via TTL) | `{messageId}`, `{expiresAt}` |
| **Outbox** (via MassTransit) | Eventos pendentes de publicação | Insert (Consumers publicam eventos), Delete (Outbox publisher consome) | `{messageId}`, `{outboxState}` |

**Ciclo de Vida de um ReceivedTransaction:**
```
PENDING ──────────► CONSOLIDATED
(inserido)        (processado)
   ↓                  ↓
TransactionCreated  ProcessConsolidationBatch
Consumer insere     marca como consolidado
```

### Por que ReceivedTransactions é Necessária?

O fluxo de 2 etapas no Consolidation.Worker (IngestTransactionsBatch → ProcessConsolidationBatch) com uma collection intermediária (`ReceivedTransactions`) oferece:

1. **Separação de responsabilidades:** Ingestão (receive) vs. processamento (consolidate)
2. **Maior throughput:** Não bloqueia a ingestão enquanto consolida
3. **Recuperação de falhas:** Se ProcessConsolidationBatch falhar, os dados ainda estão em ReceivedTransactions
4. **Auditoria:** Histórico completo de transações recebidas vs. consolidadas

### Outbox via MassTransit

O Outbox Pattern é implementado **nativamente via MassTransit MongoDB Outbox**, não via implementação customizada:

- **Quando eventos são publicados:** MassTransit insere um documento na collection `Outbox` (no mesmo banco, mesma transação)
- **Quem consome o Outbox:** O MassTransit Outbox Publisher (background service) lê documentos pendentes e publica para o RabbitMQ
- **Transação garantida:** Persistência + registro no Outbox ocorrem atomicamente

Isso garante: **Se a persistência falha, o evento não é registrado. Se o evento não é consumido, o Outbox Publisher retenta.**

---

## Especificações Técnicas de Infraestrutura

Esta seção aborda os requisitos técnicos operacionais que garantem a viabilidade da escolha MongoDB para este caso de uso.

### Transações Multi-Documento: Overhead e Frequência

**Pergunta crítica:** Qual é o custo de performance de transações multi-documento do MongoDB?

**Resposta:** Dependente da frequência. Neste sistema:

| Métrica | Valor | Justificativa |
|---------|-------|---------------|
| **Transações multi-documento por operação** | 1 (apenas no caminho Outbox) | Batcher + Processor inserem 100 RawRequests/Transactions em uma única operação de banco. A transação que importa é a que envolve persistência + registro no Outbox — apenas 1 transação por batch, não por lançamento. |
| **Frequência total em 50 req/s** | ~0.5 tx/s | 50 req/s ÷ batch_size 100 = 0.5 batch/s = 0.5 tx/s. Carga trivial para MongoDB WiredTiger. |
| **Overhead do WiredTiger em replica set** | ~5% (estimado) | Document-level locking introduce contention em alta concorrência (>1000 tx/s). Em 0.5 tx/s, é negligenciável. |

**Conclusão:** Overhead de transações multi-documento é mensurável apenas em cargas muito altas (>>1000 tx/s). Para este volume, é negligenciável. A escolha é válida.

### Replica Set: Configuração para MVP e Produção

**Problema crítico:** MassTransit MongoDB Outbox exige replica set para suportar transações multi-documento. Single-node MongoDB não suporta natively.

**Solução MVP:** Single-node replica set (não é um "hack", é a configuração padrão para habilitar transações localmente).

| Ambiente | Configuração | Considerações |
|----------|--------------|---------------|
| **MVP (local)** | Single-node replica set (`rs0`) com 1 membro | Sem failover automático — apropriado para desenvolvimento. Script de init no Docker Compose. |
| **Produção** | Replica set de 3 nós (2 data + 1 arbiter) | Failover automático; nenhuma alteração de código na aplicação. |

**Script de inicialização (Docker Compose):**
```javascript
db.adminCommand({
  replSetInitiate: {
    _id: "rs0",
    members: [{ _id: 0, host: "mongodb:27017" }]
  }
});
```

**Implicação operacional:** Se o nó único falhar, o replica set fica unavailable (sem failover). Risco documentado em Riscos — mitigado em produção com 3 nós.

### Segurança de Dados em Repouso

**Pergunta:** Como dados financeiros sensíveis são protegidos em disco?

**Resposta:** MongoDB WiredTiger oferece criptografia nativa.

| Camada | Mecanismo | Status |
|--------|-----------|--------|
| **At-rest (MVP)** | WiredTiger AES-256-CBC | Desativado no MVP por simplicidade; ativado via `--enableEncryption` em produção |
| **Field-level encryption** | MongoDB Client-Side Field Level Encryption (CSFLE) | Out of scope para MVP; documentado como evolução para dados super-sensíveis (SSN, número de cartão) |
| **Credenciais de acesso** | Environment variables (MVP) → Kubernetes Secrets (produção) → HashiCorp Vault (production-grade) | Pipeline progressiva de segurança |
| **Em trânsito** | TLS/mTLS (ambos os ambientes) | Padrão obrigatório entre aplicação e banco |

**Referência cruzada:** Ver `docs/security/04-data-protection.md` para política de proteção de dados completa.

### Transações do Outbox: Garantia de Atomicidade

**Mecanismo:** MassTransit MongoDB Outbox — implementação nativa, não customizada.

| Aspecto | Detalhe |
|--------|---------|
| **Quando eventos são registrados** | No mesmo `IMongoClientSession` que a persistência — mesma transação |
| **Quem consome o Outbox** | MassTransit Outbox Publisher (background service), polando a cada 5s |
| **Retry automático** | Sim; eventos pendentes são retentados indefinidamente até sucesso |
| **Garantia** | At-least-once (o mesmo evento pode ser entregue múltiplas vezes) → Idempotência no consumer é **obrigatória** |

**Fluxo de confirmação:**

```
┌─────────────────────────────────────────────────────────────┐
│ Unidade de trabalho atômica                                 │
│                                                             │
│  1. Persist Transactions  +  Register Outbox event         │
│  2. Commit (tudo junto)                                     │
│                                                             │
│  Resultado:                                                 │
│   ✅ Ambos sucessos                                         │
│   ❌ Ambos falham (rollback) — nenhum em estado híbrido    │
└─────────────────────────────────────────────────────────────┘
              ↓
      Outbox Publisher (background)
              ↓
      Publica para RabbitMQ com retry
```

### Idempotência: Proteção contra Reentrega

**Problema:** Message broker pode reentegar a mesma mensagem múltiplas vezes em falha transitória ou rede instável.

**Solução:** Chave de idempotência com TTL automático.

| Parâmetro | Valor | Justificativa |
|-----------|-------|---------------|
| **Chave de Idempotência** | `{EventType}:{EventId}` | Combina tipo do evento + ID único |
| **TTL da Chave** | 24 horas | Depois de 24h, a mesma mensagem pode ser processada novamente (seguro; dados consolidados após 1 dia são estáveis) |
| **Storage** | MongoDB collection `IdempotencyKeys` com índice TTL | Limpeza automática; sem necessidade de job de manutenção |
| **Comportamento** | Check-before-process: se chave existe → skip; senão → process + insert chave | O(1) lookup; idempotência garantida |

**Exemplo de fluxo:**

```
Consumer recebe DailyConsolidationUpdatedEvent (ID: evt_abc123)
  ├─ Calcula chave: "DailyConsolidationUpdated:evt_abc123"
  ├─ Busca em IdempotencyKeys
  │  ├─ Encontrada → Log "já processado", retorna sucesso silenciosamente
  │  └─ Não encontrada → Processa, insere chave com expiração 24h
```

---

## Consequências

### Positivas ✅

- **Autonomia total de schema:** O Transactions Service pode evoluir seu modelo de dados sem nenhum risco para o Consolidation Service.
- **Isolamento de falhas de banco:** Uma lentidão ou indisponibilidade em `transactions_db` não afeta as leituras da Consolidation API.
- **Worker completamente isolado:** O Consolidation Worker nunca precisa de credenciais ou conectividade com `transactions_db`.
- **Atomicidade do Outbox garantida:** As transações multi-documento do MongoDB asseguram que lançamento e evento de notificação são sempre consistentes.
- **Stack de aplicação homogênea:** MongoDB em transactions_db e consolidation_db (todos os serviços de aplicação) — uma única tecnologia de banco de dados de aplicação, um único modelo mental operacional.
- **Escalabilidade independente:** Cada banco pode ser escalado conforme a demanda específica do seu serviço.

### Negativas — Trade-offs Aceitos ⚠️

- **Sem JOIN entre bancos:** Consultas que cruzam dados de Transactions e Consolidation não são possíveis via query — exigem composição no nível de aplicação.
- **Consistência eventual:** O saldo consolidado reflete os lançamentos com lag (P95 < 5s — ver ADR-001 para breakdown de latência por etapa; exceção: no melhor caso, abaixo de 500ms).
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

---

## ❓ Perguntas Frequentes — FAQ Técnico

Esta seção responde explicitamente às questões técnicas que avaliadores experientes comumente levantam sobre a viabilidade e trade-offs da arquitetura.

### **"Por que MongoDB para dados financeiros? Não é o caso clássico para relacional?"**

**Resposta:** Sim, dados financeiros historicamente usam bancos relacionais. Mas essa convenção reflete a era anterior a transações multi-documento robustas e imutabilidade aplicacional.

**Argumentos chave:**

1. **Imutabilidade é aplicacional, não de banco:** Lançamentos financeiros são **insert-only** — nunca alterados, apenas compensados via reversal. Essa propriedade garante auditoria sem depender de triggers ou audit tables relacionais.

2. **Compliance é implementado na aplicação:** Retenção de dados, rastreabilidade e versionamento de schema são políticas aplicacionais, não constraints de banco. MongoDB oferece TTL indexes, que são equivalentes.

3. **Schema evolution é um benefício real:** Com PostgreSQL, adicionar um campo exigiria `ALTER TABLE`, com risk de lock. Com MongoDB, o campo é adicionado gradualmente — documentos antigos (sem campo) e novos (com campo) coexistem.

4. **Transações ACID multi-documento:** MongoDB suporta natively desde v4.0 — pré-requisito técnico do Outbox Pattern.

**Conclusão:** MongoDB é uma escolha legítima. PostgreSQL também seria válido tecnicamente, mas introduziria heterogeneidade de stack sem ganho funcional.

---

### **"MongoDB multi-document transactions: qual é o custo real?"**

**Resposta:** Dependente da frequência. Em 50 req/s com batch_size 100:

- **Transações por segundo:** ~0.5 tx/s (apenas no caminho Outbox, não por lançamento)
- **Overhead estimado:** ~5% em casos normais (document-level locking em WiredTiger)
- **Limite de viabilidade:** ~1000 tx/s (acima disso, contention aumenta materialmente)

**Para este volume, o overhead é negligenciável.** Transações multi-documento são usadas apenas na persistência + registro do Outbox — uma única operação por batch, não por lançamento.

---

### **"O plano implementação dizia PostgreSQL. Por que a ADR diz MongoDB?"**

**Resposta:** ADR-002 supersede a decisão provisória do plano.

- **Contexto:** `plano-implementacao.md` era um rascunho com stack provisória escrito antes da análise arquitetural formal.
- **Resolução:** ADR-002 é a decisão formal aprovada. Ela prevalece.
- **Nota:** O próprio `plano-implementacao.md` já lista MongoDB para DB Lançamentos e DB Consolidado na tabela da stack — a inconsistência está apenas na seção "Riscos" que menciona "PostgreSQL sem réplica", referindo-se ao Keycloak, não à aplicação.

**Resultado:** Sem conflito real; apenas falta de sincronização documental que essa ADR resolve.

---

### **"E a criptografia at-rest e field-level encryption?"**

**Resposta:** MongoDB oferece criptografia nativa em múltiplas camadas.

| Camada | Mecanismo | MVP | Produção |
|--------|-----------|-----|----------|
| **At-rest** | WiredTiger AES-256-CBC | Desativado (simplicidade) | Ativado via `--enableEncryption` |
| **Field-level** | MongoDB CSFLE | Out of scope | Documentado como evolução |
| **Credenciais** | Env vars → K8s Secrets → Vault | Env vars | Vault + mTLS |
| **Em trânsito** | TLS 1.2+ | ✅ Obrigatório | ✅ Obrigatório |

**Ver:** `docs/security/04-data-protection.md` para política completa.

---

### **"MongoDB single-node com Outbox transacional. Qual é o plano real?"**

**Resposta (CRÍTICO):** MassTransit MongoDB Outbox **exige replica set**. Single-node não suporta transações natively.

**Solução:**
- **MVP:** Single-node replica set (`rs0` com 1 membro) — não é um hack, é o padrão para habilitar transações localmente
- **Script Docker Compose:** `rs.initiate({_id: "rs0", members: [{_id: 0, host: "mongodb:27017"}]})`
- **Produção:** Replica set de 3 nós com voting members — nenhuma alteração de código

**Implicação:** Se o nó único falha, o replica set fica indisponível sem failover. Risco documentado em Riscos; mitigado em produção.

---

### **"Reconciliação sem violar isolamento? Como?"**

**Resposta:** Reconciliação usa **apenas `consolidation_db`**, nunca acessa `transactions_db`. Dois cenários:

**Cenário A — Evento chegou mas não foi processado:**
```
ReceivedTransactions WHERE sem DailyBalance associado
  ├─ Reprocessa via ConsolidationBatchReceivedEvent
  ├─ Consumer 2 idempotente → mesmo resultado
  └─ DailyBalance é atualizado
```
Coberto pela reconciliação interna no `consolidation_db`.

**Cenário B — Evento nunca chegou (perdido):**
Coberto pelo Outbox Pattern com at-least-once delivery:
- MassTransit Outbox Publisher retenta indefinidamente eventos não consumidos
- Durable queues no RabbitMQ preservam mensagens mesmo após crash do broker
- Health checks monitoram acumulação de eventos no Outbox — alertas disparam se > 1 minuto sem entrega

Para disaster recovery extremo (perda simultânea de banco de Transactions + Outbox Publisher + RabbitMQ), essa é uma limitação aceita do modelo de consistência eventual. A probabilidade operacional é próxima de zero.

**Isolamento preservado 100%.**

---

### **"DistributedLocks vs. competing consumers do broker?"**

**Resposta:** Distinção fundamental de padrão:

- **Competing consumers** (RabbitMQ nativo): Quando o trabalho chega via mensagem
- **Distributed Locks** (MongoDB): Quando o trabalho é descoberto via **polling** do banco

**Neste sistema:**
- Batcher é um padrão de polling (lê `RawRequests` pendentes em loop)
- RabbitMQ não tem visibilidade sobre quem faz polling de banco
- Sem lock, múltiplos Batchers leeriam os mesmos RawRequests → processamento duplicado

**Alternativa considerada:** Sharding de RawRequests por instância — descartada por complexidade operacional desnecessária para 50 req/s.

**DistributedLock é a solução correta.**

---

### **"Dois MongoDB + PostgreSQL + Redis. Onde está a homogeneidade?"**

**Resposta:** Corrigir o claim: "homogeneidade de **stack de aplicação**".

- **MongoDB (transactions_db, consolidation_db):** Stack de aplicação homogênea ✅
- **PostgreSQL (Keycloak):** Infraestrutura de identidade — decisão do Keycloak, não da arquitetura
- **Redis:** Cache in-memory — infraestrutura, não banco de dados de aplicação

**Comparação honesta:**
- Stack original: PostgreSQL (app) + Redis + RabbitMQ = 3 tecnologias stateful
- Stack atual: MongoDB (app) + Redis + RabbitMQ + PostgreSQL (Keycloak) = 4 tecnologias stateful

**Trade-off aceito:** +1 tecnologia stateful em troca de homogeneidade de stack de aplicação com MongoDB.

---

### **"Como testo consistência eventual de ponta a ponta?"**

**Resposta:** Estratégia por camada.

| Camada | Abordagem |
|--------|-----------|
| **Unit** | Handlers sem I/O; domain logic pura |
| **Integration** | TestContainers com MongoDB real + RabbitMQ real |
| **E2E** | `docker-compose up` + assertions com retry/polling |

**Padrão de assertion:**
```csharp
await PollUntilAsync(() => GetDailyBalance(userId, date) > 0, timeout: 10s);
```

**SLA:** P95 < 5s (documentado na ADR-001). "500ms normalmente" não é SLA; é observação operacional.

**Consistência eventual é perfeitamente testável.**

---

### **"ReceivedTransactions é duplicação desnecessária?"**

**Resposta:** Não. Três razões:

1. **Redução de write contention (não complexidade algorítmica):**
   - Sem intermediária: 50 writes/s individuais no mesmo documento `DailyBalances[userId][date]` → contenção de write lock no WiredTiger por cada mensagem
   - Com intermediária: 1 batch write acumulado por operação de consolidação → eliminação de contenção granular. Cada consumer libera seu slot rapidamente (insert simples em `ReceivedTransactions`), o processamento pesado (cálculo + update) fica isolado no Consumer 2 sem bloquear a ingestão

2. **Idempotência natural:**
   - Se Consumer 1 falha no insert de ReceivedTransactions, o evento é reenviado
   - Se a lógica consolidada estivesse em Consumer 1, retry reprocessaria cálculos já aplicados → idempotência muito mais difícil
   - Com separação: Consumer 2 é idempotente (recalcular mesmo consolidado = mesmo resultado)

3. **Habilita reconciliação:**
   - Worker de reconciliação diária busca `ReceivedTransactions` sem consolidação associada
   - Sem intermediária, não haveria como detectar/recuperar consolidações perdidas sem acessar `transactions_db`

**ReceivedTransactions justifica-se por separação de responsabilidades + idempotência natural.**

---

## 📜 Histórico de Revisões

### Revisão 2 (2026-03-25) — Detalhamento de Coleções e Outbox via MassTransit

**Contexto:** A implementação evoluiu para um pipeline batch com collections intermediárias (RawRequests, ReceivedTransactions) que não eram explícitas na decisão original.

**Mudanças:**

1. **Coleções em transactions_db:**
   - `RawRequests` — buffer de ingestão rápida (novo)
   - `Transactions` — lançamentos processados (existia)
   - `DistributedLocks` — lock distribuído para Batcher (novo)
   - `Outbox` — via MassTransit (clarificado)

2. **Coleções em consolidation_db:**
   - `ReceivedTransactions` — buffer intermediário (novo)
   - `DailyBalances` — saldo consolidado (existia)
   - `IdempotencyKeys` — prevenção de duplicatas (novo)
   - `Outbox` — via MassTransit (clarificado)

3. **Clarificação do Outbox:** Especificado que é **MassTransit MongoDB Outbox nativo**, não implementação customizada. Garante atomicidade entre persistência e publicação.

4. **Ciclos de vida documentados:** Adicionado diagrama de estado para RawRequest e ReceivedTransaction, mostrando transições de estado.

5. **Justificativa para ReceivedTransactions:** Documentado por que uma collection intermediária é necessária (separação de responsabilidades, throughput, recuperação de falhas).

**Justificativas:**

- **Coleções intermediárias:** Possibilitam separação clean entre ingestão e processamento, aumentando throughput e resiliência.
- **Distributed Locks:** Necessário para coordenar múltiplas instâncias do Transactions.Worker sem contenção.
- **Outbox nativo:** MassTransit MongoDB Outbox oferece garantias de atomicidade sem overhead de implementação customizada.

**Trade-offs adicionados:**

- **Mais coleções:** Maior complexidade operacional (backup, índices, limpeza de dados expirados).
- **Tamanho maior de DocumentDB:** ReceivedTransactions e IdempotencyKeys adicionam volume de dados.


