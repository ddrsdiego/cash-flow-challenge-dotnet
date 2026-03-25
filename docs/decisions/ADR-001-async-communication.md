# ADR-001: Comunicação Assíncrona via RabbitMQ com Outbox Pattern

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-001 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-25 |
| **Decisores** | Time de Arquitetura |
| **ADRs Relacionadas** | [ADR-002](ADR-002-database-per-service.md), [ADR-003](ADR-003-user-context-propagation.md) |

---

## Contexto e Problema

O sistema é composto por dois serviços com responsabilidades distintas:

- **Transactions Service** — recebe e persiste lançamentos financeiros (débitos e créditos)
- **Consolidation Service** — calcula e expõe o saldo diário consolidado

Após um lançamento ser criado, o serviço de consolidação precisa recalcular o saldo daquele dia.

### Requisito Crítico

> **"O serviço de controle de lançamentos NÃO deve ficar indisponível caso o serviço de consolidação diário esteja indisponível."**

Este requisito elimina qualquer abordagem síncrona entre os serviços, pois acoplamento temporal faria com que falhas em um serviço cascateassem para o outro.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Isolamento de falhas: Transactions não pode ser afetado pela indisponibilidade da Consolidation | RNF — Requisito principal |
| Throughput de 50 req/s na Consolidation com ≤ 5% de perda | RNF — Throughput |
| Garantia de que nenhum lançamento seja perdido para o consolidado | RNF — Confiabilidade |
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

### Opções Síncronas (descartadas)

Qualquer comunicação síncrona direta entre os serviços viola o requisito não funcional crítico: se o serviço de consolidação estiver indisponível, a criação de lançamentos falharia junto. Ambas — HTTP e gRPC — foram descartadas sem análise adicional por criarem acoplamento temporal.

### Polling Periódico

O Consolidation consultaria periodicamente os dados do Transactions para verificar novos lançamentos.

| Critério | Avaliação |
|----------|-----------|
| Isolamento de falhas | ✅ Transactions não é afetado |
| Acoplamento de dados | ❌ Consolidation precisaria acessar o banco de Transactions (violação de Database-per-Service) |
| Latência | ❌ Lag proporcional ao intervalo do poll |
| **Veredicto** | **Descartado** |

### Opções de Mensageria: RabbitMQ vs Kafka vs Azure Service Bus

| Critério | RabbitMQ | Kafka | Azure SB |
|----------|----------|-------|----------|
| Isolamento de falhas | ✅ | ✅ | ✅ |
| Garantia de entrega | ✅ | ✅ | ✅ |
| Complexidade operacional | ✅ Baixa | ❌ Alta | ⚠️ Média |
| Execução local | ✅ | ⚠️ Complexo | ❌ Não |
| Vendor lock-in | ✅ Nenhum | ✅ Nenhum | ❌ Alto |
| Fit para volume do sistema | ✅ Ideal | ❌ Over-engineering | ⚠️ |
| **Veredicto** | ✅ **Escolhido** | Descartado | Descartado |

---

## Decisão

**Adotar comunicação assíncrona exclusiva entre Transactions e Consolidation, com Outbox Pattern para garantir atomicidade entre persistência do lançamento e entrega do evento ao message broker.**

### Modelo de Comunicação — Pipeline em 4 Etapas

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PIPELINE DE COMUNICAÇÃO ASSÍNCRONA                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ETAPA 1: Ingestão Rápida (API)                                             │
│  Client → POST /transactions → Valida + persiste RawRequest → 202 Accepted │
│                                                                             │
│  ETAPA 2: Batcher (Worker background service)                              │
│  Polpa RawRequests pendentes em lote → Publica TransactionBatchReadyEvent  │
│                                                                             │
│  ETAPA 3: Processor (Worker consumer)                                       │
│  Consome batch → Mapeia + valida + persiste Transactions                   │
│  → Publica TransactionCreatedEvent                                          │
│                                                                             │
│  ETAPA 4: Consolidação (Worker com 2 consumers)                             │
│  Consumer 1: Consome TransactionCreatedEvent → Persiste ReceivedTransactions│
│  Consumer 2: Consome ConsolidationBatchReceived → Atualiza DailyBalances   │
│                                                                             │
│  ETAPA 5: Cache Invalidation (API)                                          │
│  Consome DailyConsolidationUpdatedEvent → Invalida MemoryCache             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Garantia de Atomicidade — Outbox Pattern

A persistência do lançamento e o registro da intenção de publicação do evento ocorrem dentro da mesma transação de banco. A entrega ao broker é responsabilidade de um mecanismo de retransmissão confiável que opera de forma independente.

```
Lançamento persistido  +  Evento pendente de publicação
         ↓
Confirmam ou descartam juntos
         ↓
Mecanismo de retransmissão confiável com retry
         ↓
Message Broker
```

---

## Consequências

### Positivas ✅

- **Isolamento total de falhas:** Transactions permanece 100% funcional mesmo com Consolidation indisponível
- **Absorção de picos:** Message broker atua como buffer
- **Escalabilidade independente:** Transactions e Consolidation escalam sem dependências
- **Durabilidade:** Eventos persistidos no broker
- **Observabilidade nativa:** Broker expõe métricas de profundidade de fila e throughput

### Trade-offs Aceitos ⚠️

- **Consistência eventual:** Há intervalo entre criação do lançamento e atualização do consolidado
- **Complexidade adicional:** Outbox Pattern requer mecanismo de retransmissão confiável e idempotência obrigatória
- **Novo componente operacional:** Message broker precisa ser monitorado
- **At-least-once delivery:** Idempotência no consumer é obrigatória

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Message broker indisponível | Média | Alto | Cluster HA em produção |
| Dead Letter Queue cresce sem monitoramento | Baixa | Alto | Alerta quando DLQ > 0 |
| Mecanismo de retransmissão falha | Baixa | Médio | Health check contínuo |
| Mensagem entregue múltiplas vezes sem idempotência | Alta | Alto | Chave de idempotência obrigatória |

---

## Referências

- [Outbox Pattern — microservices.io](https://microservices.io/patterns/data/transactional-outbox.html)
- [RabbitMQ Reliability Guide](https://www.rabbitmq.com/reliability.html)
- [Enterprise Integration Patterns — Gregor Hohpe](https://www.enterpriseintegrationpatterns.com/)
- Documento técnico detalhado: `docs/architecture/07-async-pipeline-details.md`
- Requisitos não funcionais: `docs/requirements/02-non-functional-requirements.md`

---

## Histórico de Revisões

### Revisão 1 (2026-03-19)
Decisão inicial de comunicação assíncrona com RabbitMQ e Outbox Pattern.

### Revisão 2 (2026-03-25)
Refatoração: Separação de responsabilidades entre ADR (decisão arquitetural) e documento técnico (especificações operacionais). Extraído conteúdo de operação e FAQ para `07-async-pipeline-details.md`.
