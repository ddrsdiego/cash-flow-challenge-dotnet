# 06 — Architectural Patterns

## Visão Geral

Este documento descreve os **padrões arquiteturais adotados** no CashFlow System, suas justificativas, trade-offs e como cada um endereça os requisitos do sistema.

Cada padrão responde a um problema específico — não foram adotados por preferência, mas por necessidade.

---

## Sumário de Padrões

| Padrão | Onde | Requisito Atendido |
|--------|------|--------------------|
| [Outbox Pattern](#1-outbox-pattern) | Transactions Service | Atomicidade entre persistência e mensageria |
| [Cache-First](#2-cache-first-pattern) | Consolidation API | 50 req/s com p95 ≤ 500ms |
| [Event-Driven + Async](#3-event-driven--comunicação-assíncrona) | Transactions → Worker | Isolamento de falhas (RNF crítico) |
| [Idempotência](#4-idempotência) | Consolidation Worker | At-least-once sem duplicação |
| [Dead Letter Queue](#5-dead-letter-queue-dlq) | RabbitMQ | Retry + observabilidade de falhas |
| [Database-per-Service](#6-database-per-service) | MongoDB | Isolamento de dados entre serviços |
| [CQRS Light](#7-cqrs-light) | Consolidation Service | Separação de leitura e escrita |
| [Circuit Breaker](#8-circuit-breaker) | Gateway + Services | Resiliência a falhas de dependências |
| [API Gateway](#9-api-gateway) | YARP | Ponto de entrada único + segurança centralizada |

---

## 1. Outbox Pattern

### Problema
Ao criar um lançamento, dois efeitos colaterais precisam acontecer de forma atômica:
1. Persistir a transação no MongoDB
2. Publicar evento `TransactionCreated` no RabbitMQ

**Sem o Outbox:**
```
INSERT MongoDB → ✅ Sucesso
PUBLISH RabbitMQ → ❌ Falha de rede

Resultado: Transação salva, mas consolidado NUNCA é recalculado.
Dados inconsistentes entre Transactions e Consolidation.
```

### Solução
```
BEGIN MongoDB session
  INSERT transactions
  INSERT outbox { event, status: PENDING }
COMMIT (atômico)

OutboxPublisher (background):
  LOOP:
    SELECT outbox WHERE status = PENDING
    PUBLISH RabbitMQ
    UPDATE outbox SET status = PUBLISHED
```

### Diagrama
```
┌──────────────────────────────────────────────────────────┐
│                  MongoDB Transaction                     │
│                                                          │
│  transactions    outbox                                  │
│  ┌──────────┐   ┌─────────────────────┐                  │
│  │ tx doc   │   │ event: PENDING      │                  │
│  └──────────┘   └─────────────────────┘                  │
│                                                          │
│  ← COMMIT ou ROLLBACK (ambos juntos) →                  │
└──────────────────────────────────────────────────────────┘
                        ↓
                OutboxPublisher
                (background)
                        ↓
                   RabbitMQ ✅
```

### Trade-offs

| Prós | Contras |
|------|---------|
| Atomicidade garantida | Adiciona latência no publish (async, não imediato) |
| Resiliência a falhas de rede | Complexidade adicional (OutboxPublisher, collection extra) |
| At-least-once delivery | Exige idempotência no consumer |
| Sem 2-Phase Commit distribuído | — |

### Alternativas Descartadas
- **2-Phase Commit:** Muito complexo e lento. Exige coordenação distribuída.
- **Saga Pattern:** Adequado para transações longas multi-step. Desnecessário aqui.
- **Direct publish (sem outbox):** Não garante atomicidade. Descartado.

---

## 2. Cache-First Pattern

### Problema
O requisito define:
> "50 req/s com no máximo 5% de perda"

Com MongoDB como única fonte de verdade, cada consulta faria uma query de leitura + aggregation, o que limita escalabilidade e aumenta latência.

### Solução
Usar Redis como cache intermediário com TTL curto:

```
GET /consolidation/daily?date=2024-03-15

1. Busca Redis (key: consolidation:2024-03-15)
   ├── HIT → retorna (< 50ms) ✅
   └── MISS:
       a. Busca MongoDB
       b. Armazena em Redis (TTL 5min)
       c. Retorna (200-500ms)
```

### Diagrama
```
Requisição de leitura
        │
        ▼
   ┌─────────┐
   │  Redis  │ ──── HIT (< 50ms) ──────────────────► Resposta
   └─────────┘
        │
       MISS
        │
        ▼
   ┌──────────┐
   │ MongoDB  │ ──── Query + Store no Redis ──────► Resposta (200-500ms)
   └──────────┘
```

### Configuração
```
TTL: 5 minutos (Redis__DefaultExpirationMinutes)
Chave: consolidation:{YYYY-MM-DD}
Política de evição: allkeys-lru (se memória cheia)
Memória máxima: 128MB
```

### Quando o Cache é Invalidado
- **Automaticamente:** Após 5 minutos (TTL)
- **Manualmente:** Consolidation Worker deleta a chave após recalcular saldo

### Trade-offs

| Prós | Contras |
|------|---------|
| Latência < 50ms no happy path | Dados podem estar defasados até 5min |
| Suporta 50+ req/s sem sobrecarregar MongoDB | Consistência eventual (não imediata) |
| Redis resiliente (126MB para consolidados diários) | Necessidade de invalidação explícita |

### Alternativas Descartadas
- **In-memory cache (.NET IMemoryCache):** Não compartilhado entre múltiplas instâncias (não escala horizontalmente)
- **Sem cache:** Limita throughput e não atende o requisito de 50 req/s com p95 ≤ 500ms

---

## 3. Event-Driven + Comunicação Assíncrona

### Problema
O requisito mais crítico do sistema:
> "O serviço de controle de lançamentos NÃO deve ficar indisponível caso o serviço de consolidado diário falhe."

Comunicação síncrona (HTTP direto) entre Transactions e Consolidation criaria acoplamento temporal — falha em um derrubaria o outro.

### Solução
Comunicação **exclusivamente assíncrona** via RabbitMQ:

```
Transactions API        RabbitMQ        Consolidation Worker
     │                     │                    │
     ├──── PUBLISH ────────►│                    │
     │     TransactionCreated                    │
     │                     │──── CONSUME ────────►
     │                     │                    │
     ◄── 201 Created (imediato)           Processa async
```

### Garantia de Isolamento
```
Cenário: Consolidation Worker DOWN

Transactions API: ✅ Continua 100% funcional
RabbitMQ:         ✅ Acumula mensagens (bounded queue)
Consolidation API:⚠️ Retorna dados do último consolidado (stale)

Quando Worker volta: Processa backlog → consolidado atualizado
```

### Topologia RabbitMQ
```
Exchange: events (type: topic)
    │
    └── Routing key: transaction.created
              │
              ▼
    Queue: consolidation.input
              │
              ├── Consumer: Consolidation Worker
              │
              └── x-dead-letter-exchange: dlx.events
                          │
                          └── Queue: dlx.transaction.created
                                      (após 3 falhas)
```

### Trade-offs

| Prós | Contras |
|------|---------|
| Isolamento total de falhas | Consistência eventual (lag de segundos) |
| Escala Transactions e Consolidation independentemente | Complexidade operacional (RabbitMQ) |
| Sem acoplamento temporal | At-least-once exige idempotência |
| RabbitMQ absorve picos de carga | — |

### Alternativas Descartadas
- **HTTP síncrono:** Cria acoplamento — se Consolidation falha, Transactions pode falhar. Descartado por violar o requisito mais crítico.
- **gRPC síncrono:** Mesmos problemas do HTTP. Descartado.
- **Polling (Transactions verifica se processou):** Anti-pattern. Cria acoplamento inverso.

---

## 4. Idempotência

### Problema
RabbitMQ usa **at-least-once delivery** — a mesma mensagem pode ser entregue mais de uma vez por:
- Falha de rede durante ACK
- Reinício do consumer
- Timeout de processamento

**Sem idempotência:**
```
Mensagem: TransactionCreated { date: 2024-03-15 }
Entregue 2x por falha de rede:

1ª entrega: balance = 650 ✅
2ª entrega: recalcula → balance = 650 (duplicação sem impacto financeiro)
            ...mas insere 2 documentos de consolidação!
```

### Solução
Cada evento carrega um `idempotencyKey` (UUID). O Worker verifica antes de processar:

```
1. Recebe evento { idempotencyKey: "uuid-123", date: "2024-03-15" }
2. SELECT processed_events WHERE key = "uuid-123"
   ├── EXISTE → ACK + ignorar silenciosamente
   └── NÃO EXISTE:
       a. Processar
       b. INSERT processed_events (na mesma transação)
       c. ACK
```

### Implementação
```
Collection: consolidation_db.processed_events
Campos:
  - _id (ObjectId)
  - idempotencyKey (string, índice único)
  - processedAt (DateTime)
  - eventType (string)

Índice TTL: expires após 7 dias (limpeza automática)
```

### Trade-offs

| Prós | Contras |
|------|---------|
| Seguro para at-least-once delivery | Collection extra com TTL |
| Processamento duplo não gera inconsistência | Lookup adicional por evento |
| Transação garante atomicidade do registro | — |

---

## 5. Dead Letter Queue (DLQ)

### Problema
Se o Consolidation Worker falha ao processar um evento (erro de DB, schema inválido, bug), a mensagem não pode ser descartada silenciosamente — perderia a consolidação daquele dia.

### Solução
RabbitMQ redireciona mensagens para DLQ após N falhas:

```
Mensagem → consolidation.input
    │
    ├── Worker processa → ACK ✅
    │
    └── Worker falha (NACK) → Retry
         └── Após 3 falhas:
              → dlx.transaction.created (Dead Letter Queue)
                  → Notificação para equipe
                  → Investigação manual
                  → Replay quando corrigido
```

### Configuração
```yaml
# RabbitMQ Queue config
Queue: consolidation.input
  x-dead-letter-exchange: dlx.events
  x-message-ttl: 86400000  # 24h antes de ir para DLQ

Dead Letter Queue: dlx.transaction.created
  Retenção: indefinida até replay manual
```

### Cenários que Vão para DLQ
1. Schema inválido (bug no código do consumer)
2. MongoDB corrompido ou indisponível após todos os retries
3. Lógica de negócio inesperada (dado inconsistente)
4. Timeout repetitivo

### Trade-offs

| Prós | Contras |
|------|---------|
| Nenhuma mensagem é perdida silenciosamente | Requer processo de monitoramento de DLQ |
| Permite replay quando bug é corrigido | Exige intervenção manual para DLQ |
| Audit trail de falhas | — |

---

## 6. Database-per-Service

### Problema
Serviços compartilhando banco de dados criam acoplamento de dados:
- Schema change em um serviço pode quebrar outro
- Não é possível escalar bancos independentemente
- Falha de conexão em um afeta todos

### Solução
Cada serviço possui seu próprio banco, com isolamento total:

```
Transactions API    ──→  transactions_db (MongoDB)
Consolidation API   ──→  consolidation_db (MongoDB)
Consolidation Worker──→  consolidation_db (MongoDB)
Keycloak            ──→  keycloak_db (PostgreSQL)
```

### Isolamento Total do Worker
O Consolidation Worker **não acessa** `transactions_db`. O evento `TransactionCreated` carrega todos os dados necessários para atualização incremental do consolidado:

```
Evento recebido: { type: "CREDIT", amount: 500.00, date: "2024-03-15" }

Worker aplica delta em consolidation_db:
  se CREDIT → totalCredits += amount
  se DEBIT  → totalDebits  += amount
  balance    = totalCredits - totalDebits
```

Essa abordagem garante:
- ✅ Isolamento completo: Worker nunca lê `transactions_db`
- ✅ Sem acoplamento de schema: mudanças no Transactions não afetam o Worker
- ✅ Consistência garantida pelo Outbox Pattern (event carrega dados suficientes)
- ✅ Operação idempotente: mesmo evento processado 2x não duplica saldo (idempotencyKey)

### Trade-offs

| Prós | Contras |
|------|---------|
| Isolamento total — falha em um não afeta outro | Sem JOIN entre databases |
| Evolução independente de schema | Consistência eventual entre databases |
| Escala independente | Evento deve carregar todos os dados necessários ao processamento |
| Worker isolado em consolidation_db (sem cross-DB read) | Mudança no contrato do evento requer versionamento |

---

## 7. CQRS Light

### Problema
O Consolidation Service tem dois padrões de acesso completamente distintos:
- **Writes:** Calculados assincronamente pelo Worker (high-consistency, baixa frequência)
- **Reads:** Consultados diretamente pelo Merchant (high-frequency, low-latency)

Misturar esses fluxos no mesmo componente criaria código confuso e dificultaria otimizações independentes.

### Solução
Separação clara entre comando (write) e consulta (read):

```
WRITE PATH (Command):
  RabbitMQ → Consolidation Worker → MongoDB (UPSERT)

READ PATH (Query):
  Merchant → Consolidation API → Redis (HIT) → Response
                               ↘ MongoDB (MISS) → Redis → Response
```

### Observação
Este não é um CQRS completo (com event store separado). É um "CQRS Light" — separação funcional sem a infraestrutura completa do padrão. Suficiente para este contexto.

### Trade-offs

| Prós | Contras |
|------|---------|
| Otimiza leitura e escrita independentemente | Consistência eventual |
| Worker pode falhar sem impactar leitura | Maior número de componentes |
| Código mais claro (responsabilidade única) | — |

---

## 8. Circuit Breaker

### Problema
Se MongoDB ou Redis ficam lentos/indisponíveis, threads ficam bloqueadas esperando timeout. Isso esgota o thread pool e o serviço inteiro fica indisponível — efeito cascata.

### Solução
Circuit Breaker interrompe chamadas para dependências com problemas:

```
Estado CLOSED (normal):
  Chamadas passam normalmente

Estado OPEN (dependência falhando):
  Chamadas retornam erro imediatamente (fast-fail)
  Sem esperar timeout

Estado HALF-OPEN (testando recuperação):
  Permite 1 chamada de teste
  Se sucesso → fecha
  Se falha → reabre
```

### Configuração Proposta (Polly)
```csharp
// MongoDB Circuit Breaker
Policy
  .Handle<MongoException>()
  .CircuitBreakerAsync(
    exceptionsAllowedBeforeBreaking: 5,
    durationOfBreak: TimeSpan.FromSeconds(30)
  )

// Redis Circuit Breaker (com fallback)
Policy
  .Handle<RedisException>()
  .CircuitBreakerAsync(
    exceptionsAllowedBeforeBreaking: 3,
    durationOfBreak: TimeSpan.FromSeconds(30),
    onBreak: (ex, _) => logger.LogWarning("Redis circuit open — using DB fallback"),
    onReset: () => logger.LogInformation("Redis circuit closed")
  )
```

### Cenário: Redis Circuit Aberto
```
1. Redis falha 3x seguidas → Circuit abre
2. Próximas chamadas de cache → fast-fail
3. ConsolidationService: detecta falha → vai direto para MongoDB
4. API continua funcionando (mais lenta, porém disponível)
5. Após 30s → circuit testa novamente
```

---

## 9. API Gateway

### Problema
Sem API Gateway:
- Cada serviço precisaria implementar autenticação, rate limiting, logging individualmente
- Múltiplos pontos de entrada expõem a superfície de ataque
- Difícil aplicar políticas cross-cutting (CORS, timeouts)

### Solução
YARP (Yet Another Reverse Proxy) como API Gateway centralizado:

```
Internet
    │
    ▼
┌─────────────────────────────────────────────┐
│  API Gateway (YARP)                         │
│                                             │
│  • Rate limiting: 100 req/s                │
│  • JWT validation (com Keycloak)           │
│  • Request logging                         │
│  • Distributed tracing (OTel)             │
│  • Routing:                                │
│    /api/v1/transactions   → Transactions API  │
│    /api/v1/consolidation  → Consolidation API │
│    /auth/*                → Keycloak          │
└─────────────────────────────────────────────┘
    │                    │
    ▼                    ▼
Transactions API    Consolidation API
```

### Benefícios
- **Segurança centralizada:** JWT validado uma vez, antes de chegar aos serviços
- **Rate limiting:** Protege os serviços de sobrecarga (100 req/s por IP)
- **Observabilidade:** Todos os traces começam no Gateway com correlationId
- **Serviços internos:** Transactions e Consolidation APIs não estão expostos diretamente

### Escolha: YARP vs Nginx vs Envoy

| | YARP | Nginx | Envoy |
|--|------|-------|-------|
| Nativo .NET | ✅ | ❌ | ❌ |
| Integração com AspNetCore | ✅ | ❌ | ❌ |
| Configuração dinâmica | ✅ | ❌ | ✅ |
| Extensibilidade C# | ✅ | ❌ | ❌ |
| Performance | Alta | Muito Alta | Muito Alta |
| Overhead de stack | Baixo | Baixo | Alto |

**Justificativa:** YARP é nativo .NET 8, permite customizações em C# (auth middleware, rate limiting, logging), sem overhead de configurar stack adicional (Lua no Nginx, Envoy sidecar). Para um sistema .NET, é a escolha mais pragmática.

---

## Resumo: Requisitos × Padrões

| Requisito | Padrão(ões) que Atende |
|-----------|----------------------|
| Transactions não cai se Consolidation cair | Event-Driven + Async, Database-per-Service |
| 50 req/s com ≤ 5% perda | Cache-First, Rate Limiting no Gateway |
| Latência p95 ≤ 500ms no consolidado | Cache-First (HIT < 50ms) |
| Dados financeiros consistentes | Outbox Pattern, Idempotência, decimal (não float) |
| Nenhuma mensagem perdida | At-least-once + DLQ + Idempotência |
| Autenticação centralizada | API Gateway + Keycloak |
| Observabilidade | OTel em todos os componentes |

---

**Próximo documento:** `docs/decisions/ADR-001-async-communication.md` (ADRs — justificativas formais de cada decisão)
