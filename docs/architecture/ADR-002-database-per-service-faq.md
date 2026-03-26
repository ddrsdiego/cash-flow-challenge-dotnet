# FAQ Técnico — ADR-002: Database-per-Service com MongoDB

Este documento responde explicitamente às questões técnicas que avaliadores experientes comumente levantam sobre a viabilidade e trade-offs da arquitetura database-per-service com MongoDB.

---

## **"Por que MongoDB para dados financeiros? Não é o caso clássico para relacional?"**

**Resposta:** Sim, dados financeiros historicamente usam bancos relacionais. Mas essa convenção reflete a era anterior a transações multi-documento robustas e imutabilidade aplicacional.

**Argumentos chave:**

1. **Imutabilidade é aplicacional, não de banco:** Lançamentos financeiros são **insert-only** — nunca alterados, apenas compensados via reversal. Essa propriedade garante auditoria sem depender de triggers ou audit tables relacionais.

2. **Compliance é implementado na aplicação:** Retenção de dados, rastreabilidade e versionamento de schema são políticas aplicacionais, não constraints de banco. MongoDB oferece TTL indexes, que são equivalentes.

3. **Schema evolution é um benefício real:** Com PostgreSQL, adicionar um campo exigiria `ALTER TABLE`, com risk de lock. Com MongoDB, o campo é adicionado gradualmente — documentos antigos (sem campo) e novos (com campo) coexistem.

4. **Transações ACID multi-documento:** MongoDB suporta natively desde v4.0 — pré-requisito técnico do Outbox Pattern.

**Conclusão:** MongoDB é uma escolha legítima. PostgreSQL também seria válido tecnicamente, mas introduziria heterogeneidade de stack sem ganho funcional.

---

## **"MongoDB multi-document transactions: qual é o custo real?"**

**Resposta:** Dependente da frequência. Em 50 req/s com batch_size 100:

- **Transações por segundo:** ~0.5 tx/s (apenas no caminho Outbox, não por lançamento)
- **Overhead estimado:** ~5% em casos normais (document-level locking em WiredTiger)
- **Limite de viabilidade:** ~1000 tx/s (acima disso, contention aumenta materialmente)

**Para este volume, o overhead é negligenciável.** Transações multi-documento são usadas apenas na persistência + registro do Outbox — uma única operação por batch, não por lançamento.

---

## **"O plano implementação dizia PostgreSQL. Por que a ADR diz MongoDB?"**

**Resposta:** ADR-002 supersede a decisão provisória do plano.

- **Contexto:** `plano-implementacao.md` era um rascunho com stack provisória escrito antes da análise arquitetural formal.
- **Resolução:** ADR-002 é a decisão formal aprovada. Ela prevalece.
- **Nota:** O próprio `plano-implementacao.md` já lista MongoDB para DB Lançamentos e DB Consolidado na tabela da stack — a inconsistência está apenas na seção "Riscos" que menciona "PostgreSQL sem réplica", referindo-se ao Keycloak, não à aplicação.

**Resultado:** Sem conflito real; apenas falta de sincronização documental que essa ADR resolve.

---

## **"E a criptografia at-rest e field-level encryption?"**

**Resposta:** MongoDB oferece criptografia nativa em múltiplas camadas.

| Camada | Mecanismo | MVP | Produção |
|--------|-----------|-----|----------|
| **At-rest** | WiredTiger AES-256-CBC | Desativado (simplicidade) | Ativado via `--enableEncryption` |
| **Field-level** | MongoDB CSFLE | Out of scope | Documentado como evolução |
| **Credenciais** | Env vars → K8s Secrets → Vault | Env vars | Vault + mTLS |
| **Em trânsito** | TLS 1.2+ | ✅ Obrigatório | ✅ Obrigatório |

**Ver:** `docs/security/04-data-protection.md` para política completa.

---

## **"MongoDB single-node com Outbox transacional. Qual é o plano real?"**

**Resposta (CRÍTICO):** MassTransit MongoDB Outbox **exige replica set**. Single-node não suporta transações natively.

**Solução:**
- **MVP:** Single-node replica set (`rs0` com 1 membro) — não é um hack, é o padrão para habilitar transações localmente
- **Script Docker Compose:** `rs.initiate({_id: "rs0", members: [{_id: 0, host: "mongodb:27017"}]})`
- **Produção:** Replica set de 3 nós com voting members — nenhuma alteração de código

**Implicação:** Se o nó único falha, o replica set fica indisponível sem failover. Risco documentado em Riscos; mitigado em produção.

---

## **"Reconciliação sem violar isolamento? Como?"**

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

## **"DistributedLocks vs. competing consumers do broker?"**

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

## **"Dois MongoDB + PostgreSQL + Redis. Onde está a homogeneidade?"**

**Resposta:** Corrigir o claim: "homogeneidade de **stack de aplicação**".

- **MongoDB (transactions_db, consolidation_db):** Stack de aplicação homogênea ✅
- **PostgreSQL (Keycloak):** Infraestrutura de identidade — decisão do Keycloak, não da arquitetura
- **Redis:** Cache in-memory — infraestrutura, não banco de dados de aplicação

**Comparação honesta:**
- Stack original: PostgreSQL (app) + Redis + RabbitMQ = 3 tecnologias stateful
- Stack atual: MongoDB (app) + Redis + RabbitMQ + PostgreSQL (Keycloak) = 4 tecnologias stateful

**Trade-off aceito:** +1 tecnologia stateful em troca de homogeneidade de stack de aplicação com MongoDB.

---

## **"Como testo consistência eventual de ponta a ponta?"**

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

## **"ReceivedTransactions é duplicação desnecessária?"**

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

## Referências

- **ADR-002:** `docs/decisions/ADR-002-database-per-service.md` — Decisão arquitetural
- **Padrões de arquitetura:** `docs/architecture/06-architectural-patterns.md`
- **Requisitos não funcionais:** `docs/requirements/02-non-functional-requirements.md`
- **Segurança de dados:** `docs/security/04-data-protection.md`


