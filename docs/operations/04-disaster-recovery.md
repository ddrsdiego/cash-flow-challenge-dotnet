# 04 — Recuperação de Falhas e Resiliência

## Visão Geral

Este documento descreve como o CashFlow System se comporta em cenários de falha, quais mecanismos de resiliência estão em vigor e como o sistema se recupera — automaticamente ou com intervenção humana — de cada tipo de falha.

Resiliência não é a ausência de falhas. É a capacidade de **continuar operando de forma degradada** quando componentes falham, e de **se recuperar automaticamente** quando possível.

O princípio central da arquitetura é que **o Transactions Service (lançamentos) não pode ser afetado pela falha do Consolidation Service**. Todos os mecanismos de resiliência descritos aqui são consequências diretas desse princípio.

---

## Matriz de Falhas por Componente

### Cenário 1: Consolidation Worker DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Transactions API** | ✅ 100% funcional — sem impacto |
| **Consolidation API** | ⚠️ Retorna dados do último consolidado (stale até Worker voltar) |
| **Lançamentos perdidos?** | ❌ Não — RabbitMQ retém mensagens indefinidamente (dentro do limite configurado) |
| **Recuperação automática** | ✅ Worker reinicia e drena o backlog da fila em ordem |
| **Impacto ao usuário** | Saldo pode estar desatualizado enquanto Worker está down |

**Mecanismo:** A comunicação assíncrona via RabbitMQ (ADR-001) garante que o Transactions Service publica eventos sem se importar com o estado do Worker. A fila atua como buffer durável. Quando o Worker volta, processa todos os eventos acumulados.

---

### Cenário 2: Consolidation API DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Transactions API** | ✅ 100% funcional — sem impacto |
| **Consolidation Worker** | ✅ 100% funcional — continua processando e atualizando o banco |
| **Lançamentos perdidos?** | ❌ Não |
| **Recuperação automática** | ✅ Restart automático (Docker: restart policy; Kubernetes: readiness probe) |
| **Impacto ao usuário** | Consultas de saldo retornam erro temporariamente |

---

### Cenário 3: RabbitMQ DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Transactions API** | ⚠️ Degradado — lançamentos criados, mas Outbox Publisher não consegue publicar |
| **Consolidation Worker** | ⚠️ Para de consumir — sem novas consolidações |
| **Lançamentos perdidos?** | ❌ Não — lançamentos persistidos no MongoDB; eventos ficam na collection outbox |
| **Recuperação automática** | ✅ Quando RabbitMQ volta, Outbox Publisher publica todos os eventos pendentes |
| **Impacto ao usuário** | Lançamentos são registrados; consolidado fica desatualizado até broker voltar |

**Mecanismo:** O Outbox Pattern (ADR-001) garante que a intenção de publicação é registrada atomicamente junto com o lançamento. Mesmo que o broker fique indisponível por horas, os eventos ficam na collection `outbox` e são publicados assim que a conectividade é restaurada.

---

### Cenário 4: MongoDB (transactions_db) DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Transactions API** | ❌ Indisponível — sem persistência |
| **Consolidation Worker** | ✅ Continua se ainda há mensagens pendentes para processar |
| **Consolidation API** | ✅ Continua se Redis tem cache válido |
| **Lançamentos perdidos?** | ❌ Requisições falham com erro explícito (não são silenciadas) |
| **Recuperação automática** | Quando MongoDB volta, Transactions API volta a funcionar normalmente |

---

### Cenário 5: MongoDB (consolidation_db) DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Transactions API** | ✅ 100% funcional — sem impacto |
| **Consolidation API** | ⚠️ Serve cache Redis (se disponível); erro se cache expirou |
| **Consolidation Worker** | ❌ Não consegue escrever — eventos acumulam na fila (retentativas com backoff) |
| **Lançamentos perdidos?** | ❌ Não — mensagens ficam na fila com retry |
| **Recuperação automática** | Worker processa backlog quando banco volta |

---

### Cenário 6: Redis DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Consolidation API** | ⚠️ Cache miss em todas as requisições — vai direto ao MongoDB |
| **Performance** | Latência aumenta (500ms em vez de < 50ms) |
| **Throughput** | Reduzido — MongoDB absorve toda a carga de leitura |
| **Lançamentos perdidos?** | ❌ Não |
| **Recuperação automática** | ✅ Quando Redis volta, cache é re-populado organicamente pelas próximas leituras |

---

### Cenário 7: Keycloak DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Novas autenticações** | ❌ Impossível — nenhum novo token pode ser emitido |
| **Requisições com token válido** | ✅ Funcionam normalmente (JWT é stateless — não consulta Keycloak por requisição) |
| **Duração da janela de tolerância** | Até 1 hora (tempo de vida do access token) |
| **Recuperação automática** | ✅ Quando Keycloak volta, novas autenticações funcionam normalmente |

**Nota:** Esta é a consequência do JWT stateless (ADR-005). O Gateway valida o token com a chave pública (em cache), sem consultar o Keycloak. A janela de tolerância é o tempo até o token expirar.

---

### Cenário 8: API Gateway DOWN

| Aspecto | Comportamento |
|---------|--------------|
| **Todos os serviços** | ❌ Inacessíveis externamente (ponto único de entrada) |
| **Processamento interno** | ✅ Worker continua processando se a fila já tem mensagens |
| **Recuperação automática** | ✅ Restart automático com Docker restart policy ou Kubernetes |
| **Tempo de recuperação** | Segundos (container restart) ou < 30s (Kubernetes pod replacement) |

---

## Mecanismos de Resiliência

### 1. Retry com Backoff Exponencial

Falhas transitórias (timeout de rede, pico de latência momentânea) são tratadas com retry automático antes de declarar falha definitiva.

**Aplicado em:**
- Outbox Publisher ao publicar para RabbitMQ
- Consolidation Worker ao escrever no MongoDB
- Serviços ao conectar ao MongoDB/Redis na inicialização

**Estratégia:**

```
Tentativa 1: imediata
Tentativa 2: após 1 segundo
Tentativa 3: após 2 segundos
Tentativa 4: após 4 segundos
Tentativa 5: após 8 segundos
→ Falha definitiva → encaminhar para DLQ (se mensagem de fila)
```

O jitter (variação aleatória) é adicionado ao backoff para evitar thundering herd — situação em que múltiplas instâncias tentam reconectar simultaneamente após a recuperação de uma dependência.

---

### 2. Dead Letter Queue (DLQ)

Mensagens que falham repetidamente são encaminhadas para uma fila de mensagens mortas, preservando o evento para análise e reprocessamento manual.

```
┌──────────────────────────────────────────────────────────────────┐
│                     FLUXO DA DLQ                                 │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Mensagem → consolidation.input                                  │
│      │                                                           │
│      ├── Worker processa → ACK ✅                                │
│      │                                                           │
│      └── Worker falha (NACK) → Tentativa 1, 2, 3...             │
│                                      │                           │
│                              Após N falhas:                      │
│                              → dlx.transaction.created           │
│                                (Dead Letter Queue)               │
│                                      │                           │
│                              → Alerta imediato (DLQ > 0)        │
│                              → Investigação manual               │
│                              → Replay quando corrigido           │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Por que DLQ é crítica:** Um evento na DLQ significa que um lançamento financeiro não foi consolidado. Isso é uma inconsistência de negócio — não apenas uma falha técnica. O alerta deve ser tratado como P1 (resposta imediata).

---

### 3. Circuit Breaker

O Circuit Breaker evita o efeito cascata: quando uma dependência está lenta ou indisponível, interrompe as chamadas a ela antes que esgote o thread pool do serviço chamador.

```
┌──────────────────────────────────────────────────────────────────┐
│  ESTADOS DO CIRCUIT BREAKER                                      │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  CLOSED (normal)                                                 │
│  → Chamadas passam normalmente                                   │
│  → Contador de falhas: 0/5                                       │
│           │                                                      │
│      5 falhas consecutivas                                       │
│           ▼                                                      │
│  OPEN (dependência com problema)                                 │
│  → Chamadas retornam erro imediatamente (fast-fail)              │
│  → Sem esperar timeout — libera thread pool                      │
│  → Duração: 30 segundos                                          │
│           │                                                      │
│      Após 30 segundos                                            │
│           ▼                                                      │
│  HALF-OPEN (testando recuperação)                                │
│  → Permite 1 chamada de teste                                    │
│  → Sucesso → volta para CLOSED                                   │
│  → Falha → volta para OPEN por mais 30 segundos                  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Comportamento de fallback quando Circuit está OPEN:**

| Serviço | Dependência | Fallback |
|---------|------------|---------|
| Consolidation API | Redis (circuit aberto) | Vai direto ao MongoDB (mais lento, mas funcional) |
| Consolidation API | MongoDB (circuit aberto) | Retorna erro explícito (503) — sem silenciar |
| Transactions API | MongoDB (circuit aberto) | Retorna erro explícito (503) — sem criar lançamento parcial |

---

### 4. Idempotência

A entrega at-least-once do RabbitMQ pode resultar na reentrega da mesma mensagem. A idempotência garante que processar a mesma mensagem múltiplas vezes produza o mesmo resultado que processá-la uma única vez.

**Mecanismo:** Cada evento carrega uma chave de idempotência única. O Worker verifica, na mesma transação que processa o evento, se aquela chave já foi processada anteriormente. Se sim, descarta silenciosamente.

**Por que isso importa:** Sem idempotência, uma falha de rede durante o ACK causaria duplicação do saldo consolidado — inconsistência financeira grave.

---

## RTO e RPO por Serviço

| Componente | RTO (Recovery Time Objective) | RPO (Recovery Point Objective) |
|-----------|-------------------------------|-------------------------------|
| API Gateway | < 30 segundos (restart automático) | Stateless — sem dados a recuperar |
| Transactions API | < 30 segundos (restart automático) | Stateless — sem dados a recuperar |
| Consolidation API | < 30 segundos (restart automático) | Stateless — sem dados a recuperar |
| Consolidation Worker | < 60 segundos | Zero — mensagens ficam na fila durante downtime |
| MongoDB | < 30 segundos (Replica Set failover em produção) | Zero com write concern majority; potencialmente segundos com standalone |
| Redis | < 30 segundos (fallback direto ao MongoDB) | Cache — dados reconstruídos automaticamente |
| RabbitMQ | < 60 segundos | Zero — mensagens persistidas em disco (durable queues) |
| Keycloak | < 60 segundos | Sessões em banco; tokens válidos continuam por até 1h |

> **Nota MVP:** Os valores de RTO assumem restart automático de container (Docker restart policy). Em produção com Kubernetes, readiness probes garantem que o pod só recebe tráfego após estar efetivamente operacional.

---

## Estratégia de Backup

| Dado | Frequência de Backup | Mecanismo | Retenção |
|------|---------------------|-----------|---------|
| `transactions_db` (MongoDB) | Diário | Dump completo + oplog contínuo | 7 anos (compliance financeiro) |
| `consolidation_db` (MongoDB) | Diário | Dump completo | 2 anos |
| `keycloak_db` (PostgreSQL) | Diário | pg_dump | 1 ano |
| Mensagens na DLQ | Manual | Snapshot antes de replay | Até resolução do incidente |

---

## Plano de Resposta a Incidentes

### Classificação de Severidade

| Severidade | Critério | Exemplo | Resposta |
|-----------|---------|---------|---------|
| **P1 — Crítico** | Sistema inacessível ou dados incorretos | API Gateway down; DLQ com mensagens | Imediata (< 15min) |
| **P2 — Alto** | Degradação severa sem dados incorretos | Latência p95 > 2s; Worker atrasado | < 30 minutos |
| **P3 — Médio** | Degradação parcial controlada | Redis down (cache miss); Keycloak reiniciando | < 2 horas |
| **P4 — Baixo** | Degradação mínima ou preventiva | Alerta de memória > 80% | < 24 horas |

### Fluxo de Resposta

```
1. DETECTAR
   → Alerta automático via Grafana (threshold ultrapassado)
   → Ou reporte de usuário

2. TRIAGEM
   → Verificar dashboard de saúde (qual componente?)
   → Verificar Seq (logs de erro com traceId)
   → Verificar Jaeger (trace do último erro)

3. CONTENÇÃO
   → Isolar o componente com problema (remover do load balancer se necessário)
   → Verificar DLQ — preservar mensagens

4. DIAGNÓSTICO
   → Correlacionar: alerta + traces + logs
   → Identificar causa raiz (bug? sobrecarga? falha de infra?)

5. RESOLUÇÃO
   → Deploy de correção ou rollback
   → Replay de mensagens da DLQ se necessário

6. POST-MORTEM
   → Documentar causa raiz, impacto e ação tomada
   → Identificar melhorias preventivas
```

---

## Limitações de Resiliência no MVP

| Limitação | Risco | Evolução para Produção |
|-----------|-------|----------------------|
| MongoDB single node | Perda de dados em falha de hardware | Replica Set com write concern majority |
| RabbitMQ single node | Mensagens perdidas se broker falha antes de persistir | Cluster com quorum queues |
| Rate limiting in-memory | Sem proteção efetiva em múltiplas instâncias | Rate limiting backed em Redis |
| Keycloak single node | SPOF de autenticação | Cluster Keycloak |
| Sem CDN | DoS de nível de rede atinge diretamente o Gateway | CDN + WAF na frente do Gateway em produção |

---

## Referências

- ADR-001 (Async Communication): `docs/decisions/ADR-001-async-communication.md` — Outbox Pattern e DLQ
- ADR-002 (Database-per-Service): `docs/decisions/ADR-002-database-per-service.md` — isolamento de falhas por banco
- ADR-004 (API Gateway): `docs/decisions/ADR-004-api-gateway.md` — SPOF e evolução para produção
- ADR-005 (Authentication): `docs/decisions/ADR-005-authentication-strategy.md` — Keycloak e JWT stateless
- Padrões arquiteturais: `docs/architecture/06-architectural-patterns.md` — Circuit Breaker (Seção 8), DLQ (Seção 5), Idempotência (Seção 4)
- Monitoramento: `docs/operations/02-monitoring-observability.md` — alertas P1 para DLQ
- Escalabilidade: `docs/operations/03-scaling-strategy.md` — comportamento sob carga
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Isolamento de falhas (Seção 1.4)
