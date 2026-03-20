# 02 — Monitoramento e Observabilidade

## Visão Geral

Este documento descreve a estratégia de monitoramento e observabilidade do CashFlow System — como o sistema é instrumentado, quais sinais são coletados, como correlacioná-los e quais alertas garantem que SLAs e comportamentos críticos de negócio sejam detectados proativamente.

A observabilidade não é opcional em sistemas distribuídos. Sem ela, a equipe opera no escuro: falhas são detectadas pelos usuários, não pelos sistemas de monitoramento, e o diagnóstico depende de tentativa e erro.

A estratégia de observabilidade do sistema é fundamentada no **ADR-006** (OpenTelemetry como camada de instrumentação) e opera sobre três pilares complementares: **traces**, **métricas** e **logs**.

---

## Os Três Pilares em Prática

### Pilar 1: Traces Distribuídos (Jaeger)

Traces permitem visualizar o caminho completo de uma requisição através de múltiplos serviços. No CashFlow System, uma única operação de criação de lançamento pode envolver 4+ componentes.

**Fluxo instrumentado — Criar Lançamento:**

```
[API Gateway]          [Transactions API]     [MongoDB]        [Outbox Publisher]    [RabbitMQ]
     │                       │                   │                    │                  │
     ├── autenticação JWT     │                   │                    │                  │
     ├── rate limit check     │                   │                    │                  │
     ├──────────────────────►│                   │                    │                  │
     │                       ├── validação input │                    │                  │
     │                       ├── begin transaction                    │                  │
     │                       ├────────────────►  │                    │                  │
     │                       │  INSERT transaction                    │                  │
     │                       ├────────────────►  │                    │                  │
     │                       │  INSERT outbox    │                    │                  │
     │                       ├── commit          │                    │                  │
     │◄──────────────────────┤ 201 Created       │                    │                  │
     │                       │                   │         ┌──────────┤                  │
     │                       │                   │         │ read outbox (pending)        │
     │                       │                   │         ├──────────────────────────► │
     │                       │                   │         │             PUBLISH event   │
     │                       │                   │         │                             │
```

**O que os traces revelam:**
- Em qual componente a latência está concentrada
- Se há timeouts específicos em chamadas ao MongoDB ou RabbitMQ
- Qual o tempo total de cada operação ponta-a-ponta
- Gargalos em picos de carga (qual componente satura primeiro)

**Traces críticos para monitorar:**
- `POST /api/v1/transactions` — deve ser < 500ms no p95
- Publicação do Outbox Publisher — deve ser < 100ms após commit
- Consumo do Worker — deve ser < 1s por evento

---

### Pilar 2: Métricas (Prometheus + Grafana)

Métricas são o meio mais eficiente para detectar degradação de performance e anomalias de forma contínua.

#### Métricas Técnicas

| Métrica | Componente | Threshold de Alerta |
|---------|-----------|---------------------|
| Latência p95 de criação de lançamento | Transactions API | > 1000ms |
| Latência p95 de consulta de consolidado | Consolidation API | > 500ms |
| Taxa de erros HTTP 5xx | Todos os serviços | > 0.5% das requisições |
| Taxa de erros HTTP 4xx | Todos os serviços | > 5% das requisições |
| Profundidade da fila de entrada | RabbitMQ | > 1000 mensagens |
| Profundidade da Dead Letter Queue | RabbitMQ | > 0 mensagens |
| Cache hit rate (Redis) | Consolidation API | < 80% |
| Latência de escrita no MongoDB | Transactions API | > 200ms no p95 |
| Taxa de conexões ativas no MongoDB | Todos | > 80% do pool |

#### Métricas de Negócio

| Métrica | Significado | Threshold de Alerta |
|---------|------------|---------------------|
| Volume de lançamentos por hora | Saúde do fluxo de negócio | Queda > 50% do baseline |
| Volume de consolidações processadas por hora | Saúde do Worker | Queda > 50% do baseline |
| Taxa de eventos na DLQ | Falhas permanentes no Worker | > 0 |
| Lançamentos criados vs. consolidações geradas | Consistência entre serviços | Divergência crescente por mais de 5min |

#### Throughput do Requisito Crítico

O requisito não funcional exige **50 req/s na Consolidation API com ≤ 5% de perda**. A métrica correspondente:

```
Dashboard: Consolidation API Throughput
- Gráfico: requisições por segundo (linha)
- Threshold: linha horizontal em 50 req/s
- Alerta: taxa de erros > 2.5 req/s (= 5% de 50)
```

---

### Pilar 3: Logs Estruturados (Seq)

Logs fornecem o contexto detalhado que métricas e traces não carregam — a mensagem de erro, o dado que causou a falha, o estado do sistema no momento do problema.

#### Formato de Log

Todos os serviços emitem logs em formato JSON estruturado, com campos consistentes:

| Campo | Descrição | Exemplo |
|-------|----------|---------|
| `timestamp` | Momento do evento | `2024-03-15T15:30:00.123Z` |
| `level` | Nível de severidade | `Information`, `Warning`, `Error` |
| `traceId` | Identificador de rastreamento distribuído | `abc123def456` |
| `userId` | Identificador do usuário autenticado | `user-42` |
| `service` | Nome do serviço origem | `transactions-api` |
| `message` | Descrição do evento | `Transaction created successfully` |
| `correlationId` | Identificador da requisição original | `req-789` |

#### Rastreabilidade Ponta-a-Ponta

O `traceId` gerado no API Gateway é propagado automaticamente via OpenTelemetry por toda a cadeia. Isso permite, no Seq, filtrar todos os logs de uma única operação — desde o Gateway até o Worker — com uma única query:

```
traceId = "abc123def456"
→ [gateway]           Received POST /api/v1/transactions
→ [transactions-api]  Transaction created, publishing outbox event
→ [outbox-publisher]  Event published to broker
→ [consolidation-worker] Processing TransactionCreated event
→ [consolidation-worker] Daily consolidation updated
```

#### Níveis de Log por Cenário

| Cenário | Nível | Ação Esperada |
|---------|-------|--------------|
| Operação concluída com sucesso | Information | Nenhuma |
| Input inválido rejeitado (400) | Warning | Nenhuma (comportamento esperado) |
| Recurso não encontrado (404) | Warning | Nenhuma (comportamento esperado) |
| Falha de infraestrutura (timeout, conexão) | Error | Investigar se persistente |
| Evento na Dead Letter Queue | Error | Alerta imediato — investigação obrigatória |
| Tentativa de acesso não autorizado | Warning | Monitorar padrão (possível ataque) |

---

## Correlação entre Pilares

A correlação entre sinais é a base do diagnóstico eficiente:

```
CENÁRIO: Pico de erros às 15h30

1. Grafana alerta: taxa de erros 5xx > 1% (Transactions API)
                          ↓
2. Jaeger: buscar traces com status=error às 15h30
   → Trace abc123: timeout no MongoDB (200ms → 2000ms)
                          ↓
3. Seq: buscar logs com traceId=abc123
   → "MongoDB connection pool exhausted — 100/100 connections in use"
                          ↓
4. Grafana: verificar métrica "MongoDB connection pool usage"
   → 100% de utilização a partir das 15h28

DIAGNÓSTICO: Pool de conexões MongoDB saturado às 15h28, causando timeouts a partir de 15h30
AÇÃO: Aumentar pool de conexões ou investigar queries lentas que não liberam conexão
```

---

## Dashboards

### Dashboard 1: Visão Geral do Sistema (Operations)

Painel principal para operação diária:
- Status de saúde de cada serviço (verde/amarelo/vermelho)
- Taxa de requisições por serviço (req/s)
- Taxa de erros por serviço (%)
- Latência p50/p95/p99 por serviço
- Profundidade das filas RabbitMQ
- Uso de memória e CPU por container

### Dashboard 2: Fluxo de Negócio (Business)

Painel para acompanhamento do fluxo financeiro:
- Volume de lançamentos por hora (créditos vs débitos)
- Volume de consolidações processadas por hora
- Lag entre criação do lançamento e atualização do consolidado
- Taxa de eventos na DLQ (deve ser sempre zero)

### Dashboard 3: SLA Compliance

Painel focado nos requisitos não funcionais:
- Throughput da Consolidation API vs SLA de 50 req/s
- Latência p95 vs SLA de 500ms
- Disponibilidade por serviço (uptime %)
- Taxa de perda de requisições vs SLA de ≤ 5%

---

## Alertas

### Alertas Críticos (P1 — Resposta imediata)

| Alerta | Condição | Impacto |
|--------|---------|---------|
| Serviço DOWN | Health check falhou por 60s | Usuários afetados |
| DLQ com mensagens | DLQ > 0 mensagens por 5min | Lançamentos não consolidados |
| Taxa de erros crítica | 5xx > 5% por 2min | Degradação severa da API |
| Latência crítica | p95 > 2000ms por 2min | Experiência degradada |

### Alertas de Atenção (P2 — Investigar em até 30min)

| Alerta | Condição | Impacto |
|--------|---------|---------|
| SLA de throughput em risco | Consolidation API < 40 req/s | Risco de violar RNF |
| Cache hit rate baixo | Redis hit rate < 70% | Carga elevada no MongoDB |
| Fila crescendo | RabbitMQ queue > 500 mensagens | Worker atrasado |
| Latência elevada | p95 > 1000ms por 5min | Degradação moderada |

### Alertas Preventivos (P3 — Planejar ação)

| Alerta | Condição | Impacto |
|--------|---------|---------|
| Pool de conexões alto | MongoDB connections > 70% | Risco de saturação |
| Memória elevada | Container > 80% do limite | Risco de OOM |
| Volume abaixo do normal | Lançamentos < 50% do baseline | Possível problema upstream |

---

## Referências

- ADR-006 (Observability Stack): `docs/decisions/ADR-006-observability-stack.md` — justificativa da escolha das ferramentas
- ADR-001 (Async Communication): `docs/decisions/ADR-001-async-communication.md` — Dead Letter Queue como sinal de falha permanente
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — SLAs de latência e throughput
- Escalabilidade: `docs/operations/03-scaling-strategy.md`
- Recuperação de falhas: `docs/operations/04-disaster-recovery.md`
