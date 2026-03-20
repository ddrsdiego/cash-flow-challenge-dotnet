# ADR-001: Comunicação Assíncrona via RabbitMQ com Outbox Pattern

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-001 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-002](ADR-002-database-per-service.md), [ADR-003](ADR-003-user-context-propagation.md) |

---

## Contexto e Problema

O sistema é composto por dois serviços com responsabilidades distintas:

- **Transactions Service** — recebe e persiste lançamentos financeiros (débitos e créditos)
- **Consolidation Service** — calcula e expõe o saldo diário consolidado

Após um lançamento ser criado, o serviço de consolidação precisa recalcular o saldo daquele dia. Isso implica que os dois serviços precisam trocar informações.

### Requisito Crítico

> **"O serviço de controle de lançamentos NÃO deve ficar indisponível caso o serviço de consolidação diário esteja indisponível."**
> — Requisito Não Funcional

Este requisito elimina qualquer abordagem onde o Transactions Service **depende diretamente** da disponibilidade do Consolidation Service. Comunicação síncrona entre os serviços cria **acoplamento temporal**: se o destino falha, o originador falha junto.

### Princípio: Atomicidade da Intenção de Negócio

O ato de registrar um lançamento é uma intenção de negócio indivisível. Ou o sistema confirma completamente — lançamento persistido e consolidação notificada — ou reverte completamente. Não existe estado intermediário aceitável.

Isso implica que a persistência do lançamento e a garantia de notificação ao serviço de consolidação devem ser tratadas como **uma única unidade de trabalho atômica**.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Isolamento de falhas: Transactions não pode ser afetado pela indisponibilidade da Consolidation | RNF — Requisito principal |
| Throughput de 50 req/s no Consolidation com ≤ 5% de perda | RNF — Throughput |
| Garantia de que nenhum lançamento seja "perdido" para o consolidado | RNF — Confiabilidade |
| Absorção de picos de carga sem degradar o serviço de lançamentos | RNF — Escalabilidade |
| Retry automático em caso de falha transitória de entrega | RNF — Resiliência |

---

## Opções Consideradas

1. **RabbitMQ com Outbox Pattern** ← **escolhida**
2. HTTP Síncrono direto (Transactions → Consolidation)
3. gRPC Síncrono (Transactions → Consolidation)
4. Polling Periódico (Consolidation consulta Transactions)
5. Apache Kafka
6. Azure Service Bus

---

## Análise Comparativa

### Opções Síncronas (descartadas por violação de requisito)

Qualquer comunicação síncrona direta entre os serviços viola o requisito não funcional mais crítico do sistema: se o serviço de consolidação estiver indisponível, a criação de lançamentos falharia junto. Ambas as opções — HTTP e gRPC — foram descartadas sem análise adicional por criarem acoplamento temporal.

---

### Polling Periódico

O Consolidation Service consultaria periodicamente os dados do Transactions Service para verificar novos lançamentos.

| Critério | Avaliação |
|----------|-----------|
| Isolamento de falhas | ✅ Transactions não é afetado |
| Acoplamento de dados | ❌ Consolidation precisaria acessar o banco de Transactions (violação de Database-per-Service) |
| Latência | ❌ Lag proporcional ao intervalo do poll |
| Carga desnecessária | ❌ Consultas periódicas mesmo sem novos dados |
| **Veredicto** | **Descartado** |

---

### Opções de Mensageria: RabbitMQ vs Kafka vs Azure Service Bus

| Critério | RabbitMQ | Apache Kafka | Azure Service Bus |
|----------|----------|--------------|-------------------|
| Isolamento de falhas | ✅ | ✅ | ✅ |
| Garantia de entrega (at-least-once) | ✅ | ✅ | ✅ |
| Dead Letter Queue nativa | ✅ | ⚠️ Configuração manual | ✅ |
| Complexidade operacional | ✅ Baixa | ❌ Alta | ⚠️ Média |
| Execução local (self-hosted) | ✅ Simples | ⚠️ Complexo | ❌ Não |
| Replay de eventos históricos | ❌ | ✅ | ❌ |
| Vendor lock-in | ✅ Nenhum | ✅ Nenhum | ❌ Alto |
| Fit para volume e complexidade do sistema | ✅ Ideal | ❌ Over-engineering | ⚠️ |
| **Veredicto** | ✅ **Escolhido** | Descartado | Descartado |

**Kafka descartado:** Exige operação de cluster (partições, replicação, coordenação), com overhead que não se justifica para o volume e complexidade do sistema. Replay de eventos históricos não é requisito.

**Azure Service Bus descartado:** Vendor lock-in com Azure. Impossível executar localmente. Não agrega valor técnico adicional para este caso de uso.

---

### Garantia de Atomicidade: Outbox Pattern vs Publicação Direta

Mesmo com mensageria assíncrona, a publicação pode falhar após a persistência:

```
Persiste lançamento no banco   → ✅ Sucesso
Publica evento no message broker → ❌ Falha de rede

Resultado: lançamento registrado, mas consolidado nunca recalculado.
```

| Abordagem | Atomicidade | Complexidade |
|-----------|-------------|--------------|
| **Outbox Pattern** | ✅ Garantida | ⚠️ Moderada |
| Publicação direta (sem outbox) | ❌ Não garantida | ✅ Simples |
| Transação Distribuída (2PC) | ✅ Garantida | ❌ Alta — impacto em performance e disponibilidade |
| Saga Pattern | ✅ Eventual | ❌ Alta — adequado para fluxos multi-etapa |

**Outbox Pattern escolhido:** Garante atomicidade registrando a intenção de publicação dentro da mesma transação de banco que persiste o lançamento. A entrega ao broker ocorre de forma confiável e independente, com retry automático em falhas transitórias.

---

## Decisão

**Adotar comunicação assíncrona exclusiva entre Transactions e Consolidation, com Outbox Pattern para garantir atomicidade entre persistência do lançamento e entrega do evento ao message broker.**

### Modelo de Comunicação

O Transactions Service publica um evento ao criar um lançamento. O Consolidation Worker consome esse evento de forma assíncrona e independente. O message broker atua como buffer de desacoplamento entre os dois serviços.

```
Transactions Service          Message Broker          Consolidation Worker
        │                            │                          │
        ├── publica evento ─────────►│                          │
        │   (TransactionCreated)     │                          │
        │                            ├── entrega evento ───────►│
        │◄── resposta imediata        │                          │
            (lançamento criado)       │                   processa de forma
                                      │                   assíncrona e
                                      │                   independente
```

### Garantia de Atomicidade — Outbox Pattern

A persistência do lançamento e o registro da intenção de publicação do evento ocorrem dentro da mesma unidade de trabalho transacional. A entrega ao broker é responsabilidade de um mecanismo de retransmissão confiável que opera de forma independente.

```
┌──────────────────────────────────────────────────────────────────┐
│                  UNIDADE DE TRABALHO ATÔMICA                     │
│                                                                  │
│   Lançamento persistido  +  Evento pendente de publicação        │
│                                                                  │
│   ← CONFIRMA ou DESCARTA ambos juntos →                          │
└──────────────────────────────────────────────────────────────────┘
                        ↓
           Mecanismo de retransmissão confiável
           (entrega o evento ao broker com retry)
                        ↓
                 Message Broker ✅
```

### Garantias Implementadas

| Garantia | Mecanismo |
|----------|-----------|
| Atomicidade — confirma tudo ou descarta tudo | Transação única: persistência + registro do evento |
| Lançamento nunca ignorado pelo consolidado | Evento registrado atomicamente; entrega garantida |
| Resiliência a falhas de rede | Retry automático sobre eventos pendentes |
| Proteção contra processamento duplicado | Idempotência por chave única no consumer |
| Eventos não descartados em falha permanente | Dead Letter Queue após esgotamento de retentativas |
| Transactions isolado de falhas da Consolidation | Comunicação puramente assíncrona |

---

## Consequências

### Positivas ✅

- **Isolamento total de falhas:** Transactions Service permanece 100% funcional mesmo com Consolidation completamente indisponível.
- **Absorção de picos:** O message broker atua como buffer — picos de criação de lançamentos não sobrecarregam o Consolidation Worker.
- **Escalabilidade independente:** Transactions e Consolidation escalam de forma completamente independente.
- **Durabilidade de mensagens:** Eventos persistidos no broker sobrevivem a reinicializações.
- **Observabilidade nativa:** O broker expõe métricas de profundidade de fila, throughput e volume de Dead Letter — visibilidade operacional sem instrumentação adicional.

### Negativas — Trade-offs Aceitos ⚠️

- **Consistência eventual:** Há um intervalo entre a criação do lançamento e a atualização do consolidado. O serviço de consolidação pode retornar dados ligeiramente desatualizados.
- **Complexidade adicional:** O Outbox Pattern requer um mecanismo de retransmissão confiável e gestão de eventos pendentes, além de idempotência obrigatória no consumer.
- **Componente adicional para operar:** O message broker é uma peça de infraestrutura que precisa ser monitorada e incluída na estratégia de disaster recovery.
- **Entrega ao menos uma vez (at-least-once):** A mesma mensagem pode ser entregue mais de uma vez. Idempotência no consumer é **obrigatória**, não opcional.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Message broker indisponível (instância única no MVP) | Média | Alto | Dead Letter Queue + retry; cluster de alta disponibilidade em produção |
| Dead Letter Queue cresce sem monitoramento | Baixa | Alto | Alerta quando DLQ > 0 mensagens |
| Mecanismo de retransmissão falha silenciosamente | Baixa | Médio | Health check; alerta se eventos pendentes acumulam por mais de X minutos |
| Mensagem entregue múltiplas vezes sem idempotência | Alta (comportamento normal do broker) | Alto | Chave de idempotência com expiração automática — **implementação obrigatória** |

---

## Referências

- [Outbox Pattern — microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [RabbitMQ Reliability Guide](https://www.rabbitmq.com/reliability.html)
- [Enterprise Integration Patterns — Gregor Hohpe](https://www.enterpriseintegrationpatterns.com/)
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md`
- Padrões arquiteturais detalhados: `docs/architecture/06-architectural-patterns.md`
