# 03 — Estratégia de Escalabilidade

## Visão Geral

Este documento descreve como o CashFlow System atende — e como evoluirá para atender — os requisitos de throughput e disponibilidade definidos, em especial o requisito crítico de **50 req/s na Consolidation API com ≤ 5% de perda**.

A estratégia de escalabilidade é guiada por uma análise de gargalos: escalar o componente errado não resolve o problema — apenas desloca o gargalo para outro ponto.

---

## Requisitos Não Funcionais que Guiam a Estratégia

| Requisito | Componente Impactado | Estratégia |
|-----------|---------------------|-----------|
| Transactions API: 100+ req/s, p95 ≤ 1000ms | API Gateway + Transactions API + MongoDB | Horizontal scaling + connection pooling |
| Consolidation API: 50 req/s, p95 ≤ 500ms, ≤ 5% perda | Consolidation API + Redis | Cache-first + horizontal scaling |
| Transactions não cai se Consolidation cair | Todos | Comunicação assíncrona + isolamento |

---

## Análise de Gargalos

Antes de definir como escalar, é fundamental identificar onde o sistema saturará primeiro.

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CAMINHO CRÍTICO DE LEITURA                       │
│             (Consolidation API — requisito 50 req/s)                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Cliente ──► API Gateway ──► Consolidation API                      │
│                                      │                              │
│                               Cache Hit? ───── Sim ──► Redis ──► ✅ │
│                                      │       (< 50ms)              │
│                                     Não                            │
│                                      │                              │
│                               MongoDB ──────────────────────────► ✅│
│                               + Store Redis                         │
│                               (200-500ms)                           │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  GARGALOS POR PRIORIDADE:                                           │
│  1. Consolidation API: instância única (MVP)                        │
│  2. Redis: miss rate alto → pressão no MongoDB                      │
│  3. MongoDB (consolidation_db): queries de leitura                  │
│  4. API Gateway: ponto único de entrada                             │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Cache-First: Principal Alavanca de Escalabilidade

O requisito de 50 req/s com p95 ≤ 500ms é atendido primariamente pela estratégia de **cache-first com Redis**, não pelo escalonamento horizontal de instâncias.

### Por que o Cache é a Solução Principal

| Cenário | Throughput Sustentável | Latência p95 |
|---------|----------------------|-------------|
| Sem cache (todo request no MongoDB) | ~20-30 req/s (limitado por I/O do DB) | 300-800ms |
| Com cache Redis (80% hit rate) | > 200 req/s por instância | < 50ms no hit |
| Com cache Redis (95% hit rate) | > 500 req/s por instância | < 20ms no hit |

**O saldo consolidado diário é naturalmente cacheável:** O saldo de um dia passado não muda. O saldo do dia corrente muda a cada novo lançamento — mas é atualizado atomicamente pelo Worker, que invalida o cache imediatamente após cada consolidação.

### Configuração do Cache

| Parâmetro | Valor | Justificativa |
|-----------|-------|--------------|
| TTL | 5 minutos | Garante que dados stale expiram mesmo sem invalidação explícita |
| Invalidação explícita | Após cada consolidação pelo Worker | Reduz lag entre lançamento e consolidado visível |
| Política de eviction | LRU (Least Recently Used) | Mantém os saldos mais consultados em memória |
| Memória máxima | 128MB | Suficiente para anos de consolidações diárias (cada entrada < 1KB) |

---

## Escalabilidade por Componente

### API Gateway

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Instâncias | 1 | ≥ 2 (HPA) |
| Rate limiting | In-memory (por instância) | Redis-backed (compartilhado) |
| Load balancer | Docker port mapping | Kubernetes Ingress + LoadBalancer Service |
| Escalabilidade | Vertical (mais CPU/RAM) | Horizontal (mais pods) |

**Gargalo potencial:** Rate limiting in-memory no MVP significa que cada instância mantém seu próprio contador. Com 2 instâncias, um cliente poderia fazer o dobro das requisições permitidas. Em produção, o rate limit precisa ser backed em Redis para ser compartilhado entre réplicas.

---

### Transactions API

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Instâncias | 1 | 2-4 (HPA baseado em CPU) |
| Sessão de estado | Stateless (sem sessão) | Stateless — escala horizontalmente sem restrição |
| MongoDB connection pool | 100 conexões | 100 × N instâncias (configurar pool adequado) |
| Gargalo provável | MongoDB write throughput | MongoDB connection pool; sharding se necessário |

**Idempotência do Outbox:** A Transactions API é stateless — qualquer instância pode processar qualquer requisição. O Outbox Pattern garante que mesmo se o request for processado por instâncias diferentes (retry após falha), a mensagem não será duplicada.

---

### Consolidation API

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Instâncias | 1 | 2-6 (HPA baseado em RPS) |
| Sessão de estado | Stateless (leitura pura) | Stateless — escala horizontalmente sem restrição |
| Cache compartilhado | Redis (externo, compartilhado) | Redis Cluster (alta disponibilidade) |
| Gargalo provável | Instância única (MVP) | Redis throughput (após cache saturação) |

**Por que escala tão bem:** A Consolidation API só faz leituras. Com cache hit rate > 90%, o MongoDB raramente é consultado — o throughput é limitado principalmente pela rede e pela capacidade do Redis, que suporta centenas de milhares de operações por segundo.

---

### Consolidation Worker

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Instâncias | 1 | 1-N consumers (baseado em profundidade de fila) |
| Paralelismo | 1 consumer thread | Configurável por consumer group |
| Idempotência | Obrigatória (chave única por evento) | Mantida — múltiplos consumers processam filas diferentes |
| Gargalo provável | Single consumer (MVP) | MongoDB write + índice de idempotência |

**Atenção ao escalar:** Múltiplos Workers processando a mesma fila em paralelo exige que cada mensagem seja processada por exatamente um Worker. O particionamento de fila (por data ou por hash do lançamento) garante que diferentes Workers processem subconjuntos não sobrepostos — preservando a idempotência sem contenção.

---

### MongoDB

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Topologia | Single node | Replica Set (1 primary + 2 secondary) |
| Leituras | Primary | Secondaries (read preference: secondaryPreferred) |
| Escritas | Primary | Primary (write concern: majority) |
| Escalabilidade | Vertical | Horizontal (sharding por data se necessário) |
| Backup | Volume Docker | Backup contínuo (oplog tailing) |

**Índices como estratégia de escalabilidade:** Um índice mal dimensionado pode tornar uma query O(n) quando deveria ser O(1). Os índices críticos são:
- `transactions`: índice em `date` (queries de lançamentos por período)
- `daily_consolidation`: índice único em `date` (upsert por data)
- `processed_events`: índice único em `idempotencyKey` (verificação de duplicatas)

---

### Redis

| Aspecto | MVP | Produção |
|---------|-----|---------|
| Topologia | Single node | Primary-Replica com Sentinel ou Redis Cluster |
| Persistência | AOF (Append-Only File) | AOF + RDB (snapshot periódico) |
| Memória | 128MB | Dimensionado conforme volume de datas no cache |
| Gargalo | Single-threaded (Redis é single-core) | Redis Cluster para distribuir carga |

---

## Capacity Planning para 50 req/s

### Modelagem de Carga

Assumindo 50 req/s sustentados na Consolidation API com distribuição uniforme:

| Métrica | Cálculo | Resultado |
|---------|---------|-----------|
| Requisições por minuto | 50 × 60 | 3.000 req/min |
| Requisições por hora | 50 × 3.600 | 180.000 req/h |
| Cache hit rate alvo | — | ≥ 90% |
| Requisições que chegam ao MongoDB | 50 × 10% | 5 req/s |
| Consultas MongoDB por dia | 5 × 86.400 | 432.000 queries/dia |

Com **90% de cache hit rate**, apenas 5 req/s chegam ao MongoDB — muito abaixo do throughput máximo de leitura do MongoDB em uma instância simples (milhares de req/s para queries com índice).

### Sizing de Recursos por Componente (Produção)

| Componente | CPU (request/limit) | Memória (request/limit) | Instâncias |
|-----------|--------------------|-----------------------|-----------|
| API Gateway | 100m / 500m | 128Mi / 256Mi | 2 |
| Transactions API | 100m / 500m | 128Mi / 256Mi | 2 |
| Consolidation API | 100m / 500m | 128Mi / 256Mi | 2 |
| Consolidation Worker | 100m / 300m | 128Mi / 256Mi | 1-2 |
| MongoDB (primary) | 500m / 2000m | 512Mi / 2Gi | 1 + 2 replicas |
| Redis | 100m / 500m | 128Mi / 256Mi | 1 + 1 replica |
| RabbitMQ | 200m / 1000m | 256Mi / 512Mi | Cluster 3 nós |

---

## Horizontal Pod Autoscaler (Produção)

Em Kubernetes, o HPA escala automaticamente baseado em métricas:

| Componente | Métrica de Escalonamento | Min Pods | Max Pods |
|-----------|------------------------|----------|---------|
| API Gateway | CPU > 70% | 2 | 6 |
| Transactions API | CPU > 70% ou RPS > 80 | 2 | 8 |
| Consolidation API | CPU > 70% ou RPS > 40 | 2 | 8 |
| Consolidation Worker | Profundidade de fila > 500 | 1 | 4 |

---

## Limitações do MVP

| Limitação | Impacto | Evolução |
|-----------|---------|---------|
| Rate limiting in-memory | Limite duplicado com múltiplas instâncias | Rate limiting backed em Redis |
| MongoDB single node | Sem replicação — SPOF de dados | Replica Set em produção |
| RabbitMQ single node | Mensagens podem ser perdidas em falha | Cluster com quorum queues |
| Redis single node | Cache perdido em reinício | Replica com failover |
| Worker single consumer | Backlog não drena em paralelo | Multiple consumers com particionamento |

---

## Referências

- ADR-001 (Async Communication): `docs/decisions/ADR-001-async-communication.md` — isolamento de falhas via mensageria
- ADR-002 (Database-per-Service): `docs/decisions/ADR-002-database-per-service.md` — escalabilidade independente por banco
- Arquitetura de containers: `docs/architecture/02-container-diagram.md` — topologia e componentes
- Padrões arquiteturais: `docs/architecture/06-architectural-patterns.md` — Cache-First (Seção 2), Circuit Breaker (Seção 8)
- Monitoramento: `docs/operations/02-monitoring-observability.md` — métricas de throughput e latência
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Seções 1 e 2
