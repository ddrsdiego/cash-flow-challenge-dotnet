# ADR-001: Comunicação Assíncrona via RabbitMQ com Outbox Pattern

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-001 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-002](ADR-002-database-per-service.md), [ADR-003](ADR-003-cqrs-consolidation.md) |

---

## Contexto e Problema

O sistema é composto por dois serviços com responsabilidades distintas:

- **Transactions Service** — recebe e persiste lançamentos financeiros (débitos e créditos)
- **Consolidation Service** — calcula e expõe o saldo diário consolidado

Após um lançamento ser criado, o serviço de consolidação precisa recalcular o saldo daquele dia. Isso implica que os dois serviços precisam trocar informações.

### Requisito Crítico

> **"O serviço de controle de lançamentos NÃO deve ficar indisponível caso o serviço de consolidação diário esteja indisponível."**
> — Requisito Não Funcional, seção 1.4

Este requisito elimina qualquer abordagem onde o Transactions Service **chama diretamente** o Consolidation Service. Comunicação síncrona entre os serviços cria **acoplamento temporal**: se o destino falha, o originador falha junto.

### Princípio: Registro de Lançamento como Intenção Atômica

O ato de registrar um lançamento é uma **intenção de negócio indivisível**. Ou confirma completamente, ou reverte completamente. Não existe estado intermediário aceitável.

```
┌─────────────────────────────────────────────────────────────────────┐
│              REGISTRAR LANÇAMENTO = INTENÇÃO ÚNICA                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐         │
│  │ Validar  │──►│ Registrar│──►│ Notificar│──►│Confirmar │         │
│  └──────────┘   └──────────┘   └──────────┘   └──────────┘         │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  SUCESSO → Lançamento confirmado + notificação garantida            │
│  FALHA   → Estado anterior preservado. Nada persiste. Nada notifica.│
└─────────────────────────────────────────────────────────────────────┘
```

Um lançamento não pode existir no sistema sem a respectiva notificação garantida ao serviço de consolidação — e vice-versa. Esse princípio governa todas as decisões técnicas subsequentes desta ADR.

### Problema Secundário: Atomicidade Técnica

Ao criar um lançamento, dois efeitos colaterais precisam acontecer como parte dessa intenção única:
1. Persistir a transação no banco de dados
2. Garantir a notificação ao Consolidation Service sobre a nova transação

Se o sistema persistir a transação mas falhar ao garantir a notificação, o consolidado nunca será recalculado — inconsistência silenciosa entre os serviços, e violação do princípio de intenção atômica.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Isolamento de falhas: Transactions não pode cair com Consolidation | RNF 1.4 — Requisito principal |
| Throughput de 50 req/s no Consolidation com ≤ 5% de perda | RNF 1.1 — Throughput |
| Garantia de entrega: nenhuma transação pode ser "perdida" para o consolidado | RNF 5.1 — Confiabilidade |
| Absorção de picos de carga sem degradar o serviço de lançamentos | RNF 2.1 — Escalabilidade |
| Retry automático em caso de falha transitória | RNF 5.2 — Dead Letter Queue |

---

## Opções Consideradas

1. **RabbitMQ com Outbox Pattern** ← **escolhida**
2. HTTP Síncrono direto (Transactions → Consolidation)
3. gRPC Síncrono (Transactions → Consolidation)
4. Polling Periódico (Consolidation lê de Transactions)
5. Apache Kafka
6. Azure Service Bus

---

## Análise Comparativa

### Opções Síncronas (descartadas por violação de requisito)

| Critério | HTTP Síncrono | gRPC Síncrono |
|----------|---------------|----------------|
| Isolamento de falhas | ❌ Consolidation down → Transactions falha | ❌ Mesmo problema |
| Acoplamento temporal | ❌ Alto | ❌ Alto |
| Requisito principal | ❌ **Viola diretamente** | ❌ **Viola diretamente** |
| Complexidade | ✅ Baixa | ⚠️ Média |
| **Veredicto** | **Descartado** | **Descartado** |

**Conclusão:** Qualquer comunicação síncrona direta entre os serviços viola o requisito não funcional mais crítico do sistema. Ambas as opções foram descartadas sem análise adicional.

---

### Opção: Polling Periódico

O Consolidation Service consultaria periodicamente o banco de dados do Transactions Service para verificar novos lançamentos.

| Critério | Polling |
|----------|---------|
| Isolamento de falhas | ✅ Transactions não é afetado |
| Acoplamento de dados | ❌ Consolidation acessa banco do Transactions (violação de Database-per-Service) |
| Latência | ❌ Lag de N segundos (intervalo do poll) |
| Carga no banco | ❌ Queries periódicas mesmo sem novos dados |
| Complexidade | ⚠️ Média |
| **Veredicto** | **Descartado** |

**Motivo do descarte:** Cria acoplamento de dados entre serviços (Consolidation precisaria acessar `transactions_db`) e gera carga desnecessária no banco sem garantia de entrega.

---

### Opções Assíncronas: RabbitMQ vs Kafka vs Azure Service Bus

| Critério | RabbitMQ | Apache Kafka | Azure Service Bus |
|----------|----------|--------------|-------------------|
| Isolamento de falhas | ✅ Total | ✅ Total | ✅ Total |
| Garantia de entrega | ✅ At-least-once | ✅ At-least-once | ✅ At-least-once |
| Dead Letter Queue nativa | ✅ Sim | ⚠️ Manual (DLT) | ✅ Sim |
| Confirmação de entrega (ACK) | ✅ Por mensagem | ✅ Por offset | ✅ Por mensagem |
| Complexidade operacional | ✅ Baixa | ❌ Alta (ZooKeeper/KRaft, partições) | ⚠️ Média |
| Self-hosted (Docker Compose) | ✅ Simples | ⚠️ Complexo | ❌ Não (Azure-only) |
| Curva de aprendizado | ✅ Baixa | ❌ Alta | ⚠️ Média |
| Replay de eventos históricos | ❌ Não | ✅ Sim | ❌ Não |
| Vendor lock-in | ✅ Nenhum | ✅ Nenhum | ❌ Alto (Azure) |
| Fit para MVP com 1 consumer | ✅ Ideal | ❌ Over-engineering | ⚠️ Aceitável |
| **Veredicto** | ✅ **Escolhido** | Descartado | Descartado |

**Kafka descartado:** Exige configuração de cluster (partições, replicação, ZooKeeper/KRaft), topic management e consumer groups. Para 1 consumer com throughput de ~50 req/s, o overhead operacional não se justifica. Replay de eventos históricos não é requisito do sistema.

**Azure Service Bus descartado:** Vendor lock-in com Azure. Impossível executar localmente sem emuladores. Custo em produção. Não agrega valor técnico além do RabbitMQ para este caso de uso.

---

### Garantia de Atomicidade: Outbox Pattern vs Direct Publish

Mesmo com RabbitMQ, existe o risco da publicação falhar após a persistência:

```
INSERT MongoDB   → ✅ Sucesso
PUBLISH RabbitMQ → ❌ Falha de rede

Resultado: Transação salva, consolidado nunca recalculado.
```

| Abordagem | Atomicidade | Complexidade |
|-----------|-------------|--------------|
| **Outbox Pattern** | ✅ Garantida (transação atômica) | ⚠️ Moderada (mecanismo de retransmissão + registro de eventos pendentes) |
| Direct Publish (sem outbox) | ❌ Não garantida | ✅ Simples |
| Transação Distribuída (2PC) | ✅ Garantida | ❌ Muito alta, impacto em performance |
| Saga Pattern | ✅ Eventual | ❌ Alta, adequada para fluxos multi-step |

**Outbox Pattern escolhido:** Único que garante atomicidade sem overhead de transações distribuídas. MongoDB suporta transações multi-documento nativas (desde v4.0), tornando a implementação direta.

---

## Decisão

**Adotar comunicação assíncrona exclusiva entre Transactions e Consolidation, com Outbox Pattern para garantia de atomicidade entre persistência e publicação de eventos.**

### Modelo de Comunicação

A comunicação entre os serviços ocorre exclusivamente por meio de um **message broker** que atua como intermediário assíncrono:

```
Transactions Service          Message Broker          Consolidation Worker
        │                           │                          │
        ├── publica evento ─────────►│                          │
        │   TransactionCreated       │                          │
        │                           ├── entrega evento ────────►│
        │                           │                          │
        ◄── resposta imediata        │                   processa de forma
            (lançamento criado)      │                   assíncrona e
                                     │                   independente
```

O broker atua como **buffer de desacoplamento**: o Transactions Service continua operacional independentemente do estado do Consolidation Worker. Mensagens que não puderem ser processadas são retidas no broker e reentregues automaticamente. Após esgotadas as tentativas de reentrega, a mensagem é direcionada para uma fila de mensagens mortas (Dead Letter), preservando o evento para análise e reprocessamento manual.

### Fluxo com Outbox Pattern

A persistência do lançamento e o registro do evento a ser publicado ocorrem dentro da **mesma unidade de trabalho transacional**. Isso garante que nunca haverá um lançamento salvo sem o respectivo evento pendente — nem o inverso.

```
┌──────────────────────────────────────────────────────────────────┐
│                  UNIDADE DE TRABALHO ATÔMICA                     │
│                                                                  │
│   Registro do lançamento    +    Evento pendente de publicação   │
│                                                                  │
│   ← CONFIRMA ou DESCARTA ambos juntos →                         │
└──────────────────────────────────────────────────────────────────┘
                        ↓
        Mecanismo de retransmissão confiável
        (verifica eventos pendentes e os entrega
         ao broker com retry automático)
                        ↓
                 Message Broker ✅
```

Após o commit da transação, um mecanismo de retransmissão confiável se responsabiliza por entregar os eventos ao broker. Falhas transitórias de rede são tratadas com retry automático. A entrega é garantida de forma eventual — normalmente em milissegundos, com tolerância a falhas do broker.

### Garantias Implementadas

| Garantia | Mecanismo |
|----------|-----------|
| **Falha em qualquer etapa preserva o estado anterior** | **Intenção atômica — confirma como um todo ou não confirma** |
| Lançamento nunca se perde para o consolidado | Evento registrado atomicamente junto com a persistência |
| Publicação resiliente a falhas de rede | Retransmissão com retry automático sobre eventos pendentes |
| Processamento sem duplicação de consolidados | Idempotência por chave única no consumer |
| Mensagens não são descartadas em falha permanente | Fila de mensagens mortas (DLQ) após esgotamento de retries |
| Transactions isolado de falhas da Consolidation | Comunicação puramente assíncrona — sem dependência de disponibilidade |

---

## Consequências

### Positivas ✅

- **Isolamento total de falhas:** Transactions Service é 100% funcional mesmo com Consolidation completamente indisponível.
- **Absorção de picos:** RabbitMQ atua como buffer — picos de criação de transações não sobrecarregam o Consolidation Worker.
- **Escalabilidade independente:** Transactions e Consolidation escalam de forma completamente independente.
- **Durabilidade:** Mensagens persistidas em disco pelo RabbitMQ — sobrevivem a restart do broker (com `durable: true`).
- **Observabilidade nativa:** RabbitMQ expõe métricas via Prometheus (porta 15692) — visibilidade de profundidade de fila, throughput e DLQ.

### Negativas — Trade-offs Aceitos ⚠️

- **Consistência eventual:** Há um lag (tipicamente < 500ms em condições normais) entre a criação do lançamento e a atualização do consolidado. O Consolidation Service pode retornar dados ligeiramente desatualizados.
- **Complexidade adicional:** O Outbox Pattern requer um mecanismo de retransmissão confiável, registro persistente de eventos pendentes e implementação de idempotência no consumer.
- **Operação do broker:** O message broker é um componente adicional que precisa ser monitorado, ter seu health verificado e estar incluído na estratégia de disaster recovery.
- **At-least-once delivery:** A mesma mensagem pode ser entregue mais de uma vez. A idempotência no consumer é **obrigatória** — não opcional.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Message broker DOWN (MVP usa single-node) | Média | Alto (lançamentos criados mas não consolidados) | Dead Letter Queue + retry; em produção usar cluster de alta disponibilidade |
| DLQ cresce sem monitoramento | Baixa | Alto (perda silenciosa) | Alertas quando DLQ > 0 mensagens |
| Mecanismo de retransmissão falha silenciosamente | Baixa | Médio (delay na publicação) | Health check do processo de retransmissão; alerta se eventos pendentes acumulam por mais de X minutos |
| Mensagem entregue 2x sem idempotência | Alta (comportamento normal do broker) | Alto (consolidado duplicado) | Chave de idempotência com expiração automática — **implementação obrigatória** |

### ADRs que Dependem desta Decisão

- **[ADR-002](ADR-002-database-per-service.md):** A decisão de usar database-per-service é parcialmente motivada pela necessidade de manter o registro de eventos pendentes (outbox) e os dados de transações no mesmo contexto transacional, garantindo atomicidade.
- **[ADR-003](ADR-003-cqrs-consolidation.md):** A separação entre Worker (write path) e API (read path) no Consolidation Service é consequência direta da comunicação assíncrona — o Worker é o consumer das mensagens RabbitMQ.

---

## Referências

- [Outbox Pattern — microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [RabbitMQ Reliability Guide](https://www.rabbitmq.com/reliability.html)
- [Dead Letter Exchanges — RabbitMQ Docs](https://www.rabbitmq.com/dlx.html)
- [MongoDB Multi-Document Transactions](https://www.mongodb.com/docs/manual/core/transactions/)
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Seções 1.4, 5.1, 5.2
- Padrão detalhado: `docs/architecture/06-architectural-patterns.md` — Seções 1 (Outbox), 3 (Event-Driven), 4 (Idempotência), 5 (DLQ)
