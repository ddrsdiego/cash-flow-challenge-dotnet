# 07: Especificações Técnicas e Operacionais — Pipeline Assíncrono

**Documento Técnico de Suporte a [ADR-001: Comunicação Assíncrona via RabbitMQ com Outbox Pattern](../decisions/ADR-001-async-communication.md)**

Este documento detalha especificações operacionais, parâmetros de configuração, tratamento de falhas e FAQ técnico para a implementação do pipeline assíncrono. Para decisões arquiteturais e justificativas estratégicas, consulte o ADR-001.

---

## 📋 Índice

1. [Distributed Lock — Coordenação do Batcher](#distributed-lock)
2. [Idempotência — Proteção contra Reentrega](#idempotência)
3. [Retry com Backoff](#retry-backoff)
4. [Dead Letter Queue](#dlq)
5. [ReceivedTransactions — Proteção contra Dados Órfãos](#received-transactions)
6. [Latência End-to-End](#latência)
7. [Backpressure](#backpressure)
8. [Rastreabilidade — OpenTelemetry](#rastreabilidade)
9. [FAQ Técnico](#faq)

---

## Distributed Lock — Coordenação do Batcher {#distributed-lock}

**Problema:** Múltiplas instâncias do Transactions.Worker podem tentar processar o mesmo RawRequest simultaneamente, levando a processamento duplicado.

**Solução:** MongoDB-based Distributed Lock com renovação automática.

| Parâmetro | Valor | Justificativa |
|-----------|-------|---------------|
| **TTL do Lock** | 60 segundos | Suficiente para um batcher completar. Se a instância morrer, o lock expira e outra elege-se. |
| **Lock Key** | `"batcher_lock"` (singleton) | Apenas uma instância de Batcher ativa por vez |
| **Heartbeat de Renovação** | A cada 30 segundos | Renova o lock antes de expirar (50% do TTL) |
| **Comportamento pós-TTL** | Election: próxima instância saudável assume | Sem paralização — ingestão continua, batching retoma automaticamente |

**Impacto em caso de falha:**

- Instância 1 morre enquanto detém o lock → Lock expira em 60s → Instância 2 detecta e assume
- **Durante esses 60s:** RawRequests são criados normalmente na API (não afeta ingestão)
- **Após 60s:** Batching retoma com a nova instância

---

## Idempotência — Proteção contra Reentrega {#idempotência}

**Problema:** O broker RabbitMQ pode entregar a mesma mensagem múltiplas vezes em caso de retry ou rede instável.

**Solução:** Chave de idempotência armazenada em MongoDB com TTL.

| Parâmetro | Valor | Justificativa |
|-----------|-------|---------------|
| **Chave de Idempotência** | `{EventType}:{TransactionBatchId}` | Combina tipo do evento + ID único do batch |
| **TTL da Chave** | 24 horas | Depois de 24h, a mesma mensagem pode ser processada novamente (seguro, dados consolidados após 1 dia são estáveis) |
| **Implementação** | MongoDB `idempotency_keys` collection com índice TTL | Limpeza automática de chaves expiradas |

**Fluxo de verificação:**

```
Consumer recebe ConsolidationBatchReceivedEvent
  ↓
Calcula chave = "ConsolidationBatchReceived:batch_xyz"
  ↓
Busca chave em idempotency_keys
  ├─ Encontrada → Log como "já processado", retorna sucesso
  └─ Não encontrada → Processa, insere chave com expiração 24h
```

---

## Retry com Backoff {#retry-backoff}

**Problema:** Falhas de rede ou timeouts podem ser transitórias.

**Solução:** Retry exponencial com jitter via MassTransit.

| Parâmetro | Valor | Justificativa |
|-----------|-------|---------------|
| **Número de Retries** | 3 tentativas | 1ª falha → aguarda 5s → 2ª falha → aguarda 30s → 3ª falha → DLQ |
| **Backoff** | Exponencial com jitter: 5s, 30s, 120s | Evita "thundering herd" |
| **Jitter** | ±20% | Dispersa retentativas aleatoriamente |
| **Timeout por tentativa** | 30 segundos | Se não completar em 30s, falha e aguarda retry |

**Timeline de exemplo:**

```
T=0s:    Tentativa 1 falha
T=5s:    Tentativa 2 falha
T=35s:   Tentativa 3 falha
T=155s:  Mensagem movida para DLQ; alerta enviado
```

---

## Dead Letter Queue {#dlq}

**Problema:** Algumas mensagens falham em todas as tentativas (ex: dado corrompido).

**Solução:** DLQ com monitoramento e processo de reconciliação manual.

| Parâmetro | Valor | Justificativa |
|-----------|-------|---------------|
| **DLQ Queue Name** | `{queue_name}.dlq` | Convenção: mesmo nome com sufixo `.dlq` |
| **Monitoramento** | Alerta quando `DLQ depth > 0` | Qualquer mensagem em DLQ é anômalia |
| **Retention** | 7 dias | Tempo suficiente para investigação |
| **Reprocessamento** | Via script manual ou RabbitMQ Management UI | Permite validar/corrigir dados antes de replay |

**Fluxo de reconciliação:**

```
DLQ Alerting → On-call investiga
  ├─ Corrige dado/infraestrutura
  └─ Re-publica a mensagem manualmente
```

---

## ReceivedTransactions — Proteção contra Dados Órfãos {#received-transactions}

**Problema:** Consumer 1 persiste `ReceivedTransactions`, mas Consumer 2 falha antes de processar.

**Solução:** Idempotência do Consumer 2 + reconciliação periódica.

| Mecanismo | Detalhe |
|-----------|---------|
| **Idempotência Consumer 2** | Chave = `{date}:{set of transaction IDs}` — reprocessar resulta no mesmo consolidado |
| **Worker de Reconciliação** | Job diário que busca `ReceivedTransactions` sem consolidação associada e reprocessa |
| **SLA de Reconciliação** | Máximo 24 horas — garantia de que nenhuma transação fica órfã permanentemente |

**Fluxo de detecção:**

```
Diariamente (02:00 UTC):
  Busca ReceivedTransactions sem DailyBalance associado
    ├─ Se encontrado → Reprocessa
    └─ Se nenhum → OK
```

---

## Latência End-to-End {#latência}

### Pergunta Crítica
Quanto tempo até um lançamento aparecer no consolidado?

### Resposta por Etapa

| Etapa | Latência Típica | Latência P95 | Bottleneck |
|-------|-----------------|-------------|-----------|
| **1. API ingestão** | 10ms | 20ms | I/O do banco |
| **2. Batcher** | 500ms | 1000ms | Aguarda 100 items OU timeout 5s |
| **3. Processor** | 200ms | 400ms | Validação + mapeamento |
| **4. Consolidation Consumer 1** | 100ms | 200ms | Persistência |
| **5. Consolidation Consumer 2** | 400ms | 600ms | Agrupamento + cálculo |
| **6. Cache invalidation** | 50ms | 100ms | MemoryCache update |
| **TOTAL** | **~1.26s** | **~2.32s** | Processamento natural |

### Comportamento Adaptativo

- **Volume alto (50+ req/s):** Batcher acumula 100 itens em ~2s
- **Volume médio (10-20 req/s):** Batcher aguarda timeout 5s
- **Volume baixo (< 5 req/s):** Batcher dispara no timeout 5s

**SLA:** P95 < 5 segundos para qualquer volume

---

## Backpressure — Proteção contra Thundering Herd {#backpressure}

**Problema:** Se o broker falha e recupera, todos os eventos acumulados disparam simultaneamente.

**Solução:** Rate limiting nativo via MassTransit + Outbox relay com backoff.

| Mecanismo | Configuração | Efeito |
|-----------|--------------|--------|
| **Prefetch Count** | 100 mensagens máximo | Limita concorrência |
| **Batch Size** | 100 transações máximo | Garante latência previsível |
| **Outbox Relay Interval** | 5 segundos | Pull controlado |
| **Retry Backoff** | Exponencial (5s → 30s → 120s) | Spread load ao longo do tempo |

**Resultado:** Após recovery do broker, events são entregues em ondas controladas.

---

## Rastreabilidade — OpenTelemetry Integration {#rastreabilidade}

**Problema:** Um lançamento atravessa 5 etapas; como rastrear seu caminho?

**Solução:** TraceId e SpanId propagados como headers via W3C Trace Context.

| Componente | Implementação |
|-----------|---------------|
| **Propagação de TraceId** | MassTransit configura `W3C Trace Context` headers automaticamente |
| **Extração no Consumer** | MassTransit extrai `traceparent` header e continua o trace |
| **Visualização** | Jaeger/Zipkin mostra trace único desde POST /transactions até cache invalidation |

**Fluxo de trace visual:**

```
POST /transactions (TraceId: abc123)
  ├─ Batcher (span filha)
  ├─ TransactionBatchReadyEvent (traceparent: abc123)
  ├─ Processor (span filha)
  ├─ TransactionCreatedEvent (traceparent: abc123)
  ├─ Consolidation.IngestBatch (span filha)
  ├─ ConsolidationBatchReceivedEvent (traceparent: abc123)
  ├─ Consolidation.ProcessBatch (span filha)
  ├─ DailyConsolidationUpdatedEvent (traceparent: abc123)
  └─ Cache Invalidation (span final)
```

**Resultado:** Um único TraceId conecta todas as 5 etapas.

---

## FAQ Técnico {#faq}

### "Por que 5 etapas para 50 req/s?"

Não é sobre volume — é sobre isolamento de falhas e desacoplamento de ritmo.

- Um job cron falharia completamente em caso de erro
- O pipeline permite que a API continue 100% funcional mesmo com a consolidação quebrada
- Cada etapa escala independentemente

---

### "O Distributed Lock é um single point of failure disfarçado?"

Não. O lock é mecanismo de coordenação, não componente crítico.

| Cenário | O que acontece |
|---------|---|
| Lock fica "preso" | TTL 60s expira → Próxima instância assume |
| MongoDB indisponível | Batcher falha, mas API continua recebendo lançamentos |
| Lock nunca é liberado | Heartbeat a cada 30s renova; se nenhum heartbeat em 60s, expira |

**Diferença crítica:** Um componente crítico paralisa o sistema. O lock apenas pausa o batching — a API nunca fica indisponível.

---

### "Qual é a latência real que o comerciante vai perceber?"

P95 < 5 segundos do POST até consolidado refletido — **independente do volume**.

| Cenário | Latência |
|---------|----------|
| Lançamento no meio de um batch | 0.5s |
| Lançamento isolado | ~2.3s |
| P95 (volume nominal 50 req/s) | < 2.3s |
| P95 (volume baixo) | ~2.3s |

---

### "Se o consolidado fica desatualizado, como o comerciante evita decisões erradas?"

Duas estratégias:

1. **Indicador visual:** API retorna `"consolidation_updated_at": "2026-03-25T15:30:12Z"`
2. **Garantia eventual:** P95 < 5s garante que está praticamente atualizado

---

### "E se o lock ficar preso? Todos os batches param até expirar?"

Não. TTL 60s é curto o suficiente.

- **Batcher não dispara por 60s** → RawRequests acumulam (OK, fila absorve)
- **Após 60s** → Próxima instância saudável assume
- **API não é afetada** → Continua persistindo RawRequests

**SLA:** Máximo 60s de "pausa". Em 50 req/s, até 3000 RawRequests pendentes — manejável.

---

### "Como vocês garantem que nenhuma transação fica órfã?"

Três mecanismos em camadas:

1. **Atomicidade Outbox:** Evento persistido atomicamente com RawRequest
2. **Idempotência Consumer 2:** Reprocessar resulta no mesmo consolidado
3. **Worker de Reconciliação:** A cada dia, busca ReceivedTransactions sem consolidação e reprocessa

**Garantia:** Máximo 24 horas de lag para consolidação.

---

### "Se a DLQ tem mensagens, como o on-call sabe reprocessar?"

Processo documentado:

1. **Alerta automático:** Qualquer mensagem em DLQ ativa on-call
2. **Investigação:** Log conterá motivo (ex: "banco lento", "timeout", "dado corrompido")
3. **Ação:** Corrige infraestrutura ou dado, depois replay via RabbitMQ Management UI ou script

**SLA:** < 1 hora de resolução

---

### "Qual é o Prefetch Count e como evita thundering herd?"

| Parâmetro | Valor | Efeito |
|-----------|-------|--------|
| Prefetch Count | 100 | Máximo de 100 mensagens em processamento simultâneo |
| Batch Size | 100 | Cada batch processa máximo 100 transações |
| Outbox Relay Interval | 5 segundos | Evento entregue a cada 5s |

**Scenario após recovery do broker:**

```
T=0s:    Broker retorna, 50.000 eventos em Outbox
T=5s:    Relay publica 100 eventos
T=10s:   Relay publica próximos 100
...
T=2500s: Todos os 50.000 entregues em ondas de 100
```

---

### "Como vocês rastreiam uma transação através de 5 etapas?"

W3C Trace Context via MassTransit — um único TraceId conecta todas as etapas. Query no Jaeger mostra:
- Latência total
- Latência por etapa
- Qualquer falha no trace

---

### "Como vocês reconciliam RawRequests, Transactions e ReceivedTransactions?"

Cada uma é um estágio diferente com controle de versão:

| Entidade | Criada por | Status | Validação |
|----------|-----------|--------|-----------|
| **RawRequest** | API | `pending` ou `processed` | Campo `processed_at` |
| **Transaction** | Processor | `active` | Versão validada |
| **ReceivedTransaction** | Consumer 1 | `received` | Pronta para consolidação |
| **DailyBalance** | Consumer 2 | `consolidated` | Consolidado final |

**Reconciliação:** Worker diário valida essas invariantes e alerta se quebradas.

---

### "O Outbox usa polling? Qual é o SLA de detecção?"

Sim, MassTransit Outbox usa polling:

| Parâmetro | Valor |
|-----------|-------|
| **Poll Interval** | 5 segundos |
| **Health Check** | Alerta se eventos acumulam > 1 minuto |
| **Max Event Age** | 7 dias |

**SLA:** Detecção de anomalia em < 60 segundos.

---

### "E se ReceivedTransactions for reprocessada duplicadamente?"

Idempotência do Consumer 2 garante resultado idêntico:

```
Primeira execução:
  [T1, T2, T3] → Consolidado: $1000
  Insere chave: "2026-03-25:T1,T2,T3"

Reprocessamento (mesmo evento):
  [T1, T2, T3] → Encontra chave → Retorna sucesso (sem reprocessar)
```

**Resultado:** Consolidado permanece $1000 (idêntico).

---

### "Se o Batcher tem apenas uma instância, como escala?"

Batcher não é o bottleneck — Processor é escalável:

| Componente | Instâncias | Escalabilidade |
|-----------|-----------|---|
| **Batcher** | 1 (lock singleton) | Throughput fixo ~300 items/min |
| **Processor** | N | Escala horizontalmente |
| **Consolidation** | N | Escala horizontalmente |

Para escalar Batcher: Use sharding — múltiplos Batchers com locks distintos.

---

## Histórico de Revisões

### Versão 1.0 (2026-03-25)
Documento inicial com especificações operacionais e FAQ técnico, extraído do ADR-001 para manter separação de responsabilidades entre decisão arquitetural e detalhes de implementação.

---

**Última atualização:** 2026-03-25  
**Mantido por:** Time de Arquitetura  
**Relacionado:** [ADR-001: Comunicação Assíncrona](../decisions/ADR-001-async-communication.md)
