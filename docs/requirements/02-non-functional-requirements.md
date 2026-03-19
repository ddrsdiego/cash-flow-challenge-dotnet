# Requisitos Não Funcionais

## 1. Performance

### 1.1 Throughput
| Serviço | Requisito | Justificativa |
|---------|-----------|--------------|
| **Transactions API** | Mínimo 100 req/s | Espaço para crescimento, picos de 2x carga normal |
| **Consolidation API** | Mínimo 50 req/s (crítico) | Requisito explícito: "50 req/s com ≤5% perda" |
| **API Gateway** | Mínimo 150 req/s | Soma: Transactions (100) + Consolidation (50) |

**Nota:** 5% de perda aceitável significa que de 100 requisições, até 5 podem falhar com `429 Too Many Requests` (rate limit exceeded), mas as demais devem ser processadas com sucesso.

### 1.2 Latência
| Métrica | Consolidation | Transactions | Justificativa |
|---------|---------------|--------------|--------------|
| **p50 (mediana)** | ≤ 100ms | ≤ 200ms | Cache hit típico |
| **p95** | ≤ 500ms | ≤ 1000ms | 95% das requisições |
| **p99** | ≤ 1500ms | ≤ 2000ms | 99% das requisições |
| **Máximo (timeout)** | 5s | 5s | Além disso, requisição é abortada |

**Cache hit Consolidation:** < 50ms (Redis lookup)  
**Cache miss Consolidation:** 200-500ms (MongoDB + aggregation)

### 1.3 Disponibilidade (Uptime)

| Serviço | SLA | Downtime Aceitável/Mês |
|---------|-----|------------------------|
| **Transactions API** | 99.9% | ≤ ~43 segundos |
| **Consolidation API** | 99.5% | ≤ ~3.6 minutos |
| **API Gateway** | 99.99% | ≤ ~4 segundos |

**Justificativa:**
- Transactions é crítico: comerciante NÃO pode registrar vendas
- Consolidation é importante mas não crítico: saldo pode estar 1-2h desatualizado
- Gateway é frente pública: deve estar sempre disponível para aceitar requisições

### 1.4 Isolamento de Falhas (Resiliência)

**Requisito Principal:** "Lançamentos NÃO pode ficar indisponível se Consolidado falhar"

#### 4.1 Cenários de Falha e Comportamento

| Cenário | Transactions | Consolidation | Comportamento Esperado |
|---------|--------------|----------------|----------------------|
| Consolidation API DOWN | ✅ Operacional | ❌ DOWN | Lançamentos continua 100% funcional; consolidado retorna 503 |
| Consolidation Worker DOWN | ✅ Operacional | ⚠️ Desatualizado | Mensagens acumulam em fila; saldo é do último cálculo bem-sucedido (pode estar velha) |
| Transactions API DOWN | ❌ DOWN | ✅ Operacional | Consultas de saldo funcionam; lançamentos não podem ser criados |
| MongoDB DOWN | ❌ DOWN | ❌ DOWN | Ambos falham (compartilham BD, mas em databases diferentes) |
| RabbitMQ DOWN | ⚠️ Degradado | ⚠️ Degradado | Lançamentos são criados mas não propagam para consolidado |
| Redis DOWN | ✅ Funcional | ⚠️ Sem cache | Consolidation API funciona mas mais lentamente (sem cache) |

#### 4.2 Implementação Técnica

**Padrão: Circuit Breaker**
- Consolidation API: timeout em 5s para dependências externas
- Se 50% de requisições falham em 30s, circuit abre (fast-fail)
- Retry após 60s
- Fallback: retorna resultado em cache antigo ou erro gracioso

**Padrão: Bulkhead (Isolamento de Threads)**
- Transactions: thread pool isolado
- Consolidation: thread pool isolado
- Uma não consome threads da outra

**Padrão: Retry com Exponential Backoff**
- RabbitMQ: 3 retries com delays 1s, 2s, 4s
- MongoDB: 2 retries com delay 500ms
- APIs externas: 1 retry com delay 1s (timeout rápido)
- Dead Letter Queue: mensagens com 3+ falhas vão para DLQ para análise manual

---

## 2. Escalabilidade

### 2.1 Capacidade de Crescimento

**Previsão de Crescimento (Próximos 12 meses):**
| Período | Transações/dia | Requisições/s |
|---------|---|---|
| Hoje (MVP) | 1.000 | 50 req/s |
| 3 meses | 5.000 | 250 req/s |
| 6 meses | 20.000 | 1.000 req/s |
| 12 meses | 100.000 | 5.000 req/s |

**Architeture deve suportar:**
- Scaling horizontal de Consolidation API (adicionar instâncias)
- Scaling horizontal de Transactions API (load balancing)
- Replicação read em MongoDB (read replicas)
- Redis em cluster mode (se cache ficar gargalo)

### 2.2 Horizontal Scaling

**Transactions API:**
- Stateless (sem sessão em memória)
- Pode-se adicionar N instâncias atrás de load balancer
- Estimativa: 1 instância = 100 req/s → 50 instâncias para 5.000 req/s

**Consolidation API:**
- Stateless para requisições de leitura
- Pode-se adicionar N instâncias
- Cache (Redis) é compartilhado (não replicado por instância)
- Estimativa: 1 instância = 50 req/s com cache → 100 instâncias para 5.000 req/s

**Consolidation Worker:**
- Consumer de mensagens (não stateless)
- Pode-se adicionar N instâncias (cada uma consome mensagens diferentes)
- Auto-scaling: baseado em comprimento da fila RabbitMQ

### 2.3 Persistência em Disco

| Serviço | Storage Estimado | Crescimento | Política |
|---------|---|---|---|
| **transactions_db** | 10 MB (1.000 transações) | 1 GB/mês | Retenção: 7 anos (compliance financeira); replicação de leitura em 6 meses |
| **consolidation_db** | 1 MB (365 registros/ano) | ~100 KB/mês | Retenção: indefinida; índices em `date` e merchant |
| **Redis** | 10 MB (cache com TTL 5min) | Constante | Não cresce (LRU eviction) |

---

## 3. Segurança

### 3.1 Autenticação
- **Método:** OAuth 2.0 com OpenID Connect (Keycloak)
- **Token:** JWT (JSON Web Token)
- **Validade:** 1 hora
- **Refresh:** Via refresh token (7 dias)
- **Armazenamento:** Header `Authorization: Bearer {token}`

### 3.2 Autorização
- **Modelo:** RBAC (Role-Based Access Control)
- **Roles:** 
  - `transactions:read` — Consultar transações
  - `transactions:write` — Criar transações
  - `consolidation:read` — Consultar saldo diário
  - `admin` — Acesso total (desenvolvimento/observabilidade)

**Mapeamento:**
- Comerciante: `transactions:read`, `transactions:write`, `consolidation:read`
- Admin: todas as roles

### 3.3 Proteção de Dados em Trânsito
- **HTTPS/TLS 1.3** obrigatório para todas as APIs públicas
- **mTLS** entre serviços internos (Transactions ↔ Consolidation via RabbitMQ — não direto)
- **Certificados autoassinados** em desenvolvimento; CA confiável em produção

### 3.4 Proteção de Dados em Repouso
- **MongoDB encryption at rest:** Desabilitar em dev; ativar em produção
- **Redis encryption:** Usar requirepass (protege contra acesso não autorizado na rede)
- **Backup:** Criptografado com chave master (KMS)

### 3.5 Prevenção de Ataques Comuns

| Ataque | Mitigação |
|--------|-----------|
| **SQL Injection** | Usar MongoDB (queries parametrizadas via driver); validar input |
| **CSRF** | CORS configurado; SameSite=Strict em cookies; POST requer token |
| **XSS** | Frontend fora do escopo; APIs retornam JSON (não HTML); headers Content-Type: application/json |
| **DDoS** | Rate limiting no API Gateway; Cloud CDN (futuro) |
| **Broken Auth** | JWT validação em middleware; refresh token rotation |
| **Sensitive Data Exposure** | Descrição de erro genérica; não expõe stack traces; audit logging de acesso |

### 3.6 Auditoria e Logging
- **Eventos auditados:**
  - Login/logout (Keycloak)
  - Criação de transação
  - Acesso a dados sensíveis (consolidado)
  - Erro de autenticação (falha de login)
  - Erro de autorização (acesso negado)

- **Informações logadas:**
  - `timestamp`, `userId`, `action`, `resource`, `result` (success/failure), `sourceIp`
  - Não logar: senha, token completo, PII (exceto ID)

- **Retenção:**
  - Logs de aplicação: 30 dias em Seq
  - Logs de auditoria: 2 anos (compliance)

### 3.7 Rate Limiting
- **Consolidation API:** 50 req/s por IP (global)
- **Transactions API:** 100 req/s por usuário/token
- **API Gateway:** 500 req/s por IP (proteção global)
- **Retry:** Usando header `Retry-After: {segundos}`

---

## 4. Observabilidade

### 4.1 Métricas (Prometheus)

**Métricas Obrigatórias (por serviço):**
- `http_requests_total` — Total de requisições (by method, endpoint, status)
- `http_request_duration_seconds` — Latência (histograma com buckets: 0.01, 0.05, 0.1, 0.5, 1, 5)
- `mongodb_query_duration_seconds` — Latência de queries MongoDB
- `rabbitmq_published_messages_total` — Total de mensagens publicadas
- `rabbitmq_consumed_messages_total` — Total de mensagens consumidas
- `cache_hits_total` / `cache_misses_total` — Taxa de acerto de cache
- `active_connections` — Conexões ativas (DB, Redis, RabbitMQ)

**Alertas Críticos:**
- Erro rate > 5% no Transactions API
- Erro rate > 10% no Consolidation API
- Latência p95 > 1000ms em Transactions
- Latência p95 > 500ms em Consolidation
- Fila RabbitMQ > 10.000 mensagens (backlog)
- Cache hit rate < 50% (indica problema de invalidação)

### 4.2 Tracing Distribuído (Jaeger/OpenTelemetry)

**Spans Obrigatórios:**
- Request HTTP (entrada)
- Validação de entrada
- Autenticação/autorização
- Query MongoDB
- Publicação em RabbitMQ
- Consulta de cache
- Response HTTP

**Baggage (contexto propagado):**
- `userId` — ID do usuário autenticado
- `traceId` — ID único da requisição
- `spanId` — ID do span atual
- `parentSpanId` — Rastreamento de hierarquia

**Exemplo de trace:**
```
POST /api/transactions (50ms)
├─ Validate Input (2ms)
├─ Authenticate Token (5ms)
├─ Authorize Permission (1ms)
├─ Insert MongoDB (20ms)
│  ├─ Connect Pool (0ms)
│  └─ Insert Document (18ms)
├─ Publish RabbitMQ (15ms)
│  ├─ Connect Pool (0ms)
│  └─ Publish Message (12ms)
└─ Return Response (2ms)
```

### 4.3 Logs Estruturados (Seq/ELK)

**Formato:** JSON estruturado
```json
{
  "timestamp": "2024-03-15T15:30:45.123Z",
  "level": "INFO",
  "service": "transactions-api",
  "traceId": "550e8400-e29b-41d4-a716-446655440000",
  "userId": "user-123",
  "action": "TransactionCreated",
  "message": "Lançamento criado com sucesso",
  "data": {
    "transactionId": "507f1f77bcf86cd799439011",
    "amount": 500.00,
    "type": "CREDIT"
  },
  "duration_ms": 45
}
```

**Níveis de Log:**
- `DEBUG` — Rastreamento detalhado (desabilitado em produção)
- `INFO` — Eventos importantes (criação de transação, consolidação completa)
- `WARN` — Situações anômalas (cache miss recorrente, retry ocorrendo)
- `ERROR` — Falhas não críticas (timeout em API externa, falha de persistência com retry bem-sucedido)
- `FATAL` — Falhas críticas (database down, RabbitMQ down)

---

## 5. Confiabilidade

### 5.1 Padrão: Outbox (Garantia de Publicação)

**Problema:** Se salvar transação funciona mas publicar mensagem falha, consolidado nunca recalcula

**Solução: Outbox Pattern**
```
BEGIN TRANSACTION
  1. Insert Transaction
  2. Insert Event (em tabela outbox)
COMMIT

// Depois (background):
  3. Lee Event do outbox
  4. Publica em RabbitMQ
  5. Se sucesso: delete do outbox
  6. Se falha: retry (exponential backoff)
```

**Implementação em MongoDB:**
- `transactions` collection — documentos de transação
- `outbox` collection — eventos aguardando publicação
- Transação MongoDB garante atomicidade

### 5.2 Padrão: Dead Letter Queue (DLQ)

**Fluxo com falhas:**
```
RabbitMQ: transaction.created
  ↓
Consolidation Worker (consome)
  ↓
Falha 3x? → DLQ (dlx.transaction.created)
  ↓
Admin notificado → Investigação manual
```

**Exemplo:** Worker faz queryr MongoDB com erro de schema? Mensagem não pode ser processada. DLQ evita perder a mensagem e alertar admin.

### 5.3 Padrão: Idempotência

**Requisito:** Se worker processa mesma mensagem 2x, não duplicar consolidado

**Solução:**
- Cada evento tem `idempotencyKey` (UUID gerado pelo cliente)
- Consolidation worker: antes de processar, verifica `processed_events[idempotencyKey]`
- Se já processado: ignora silenciosamente
- Se novo: processa e registra

---

## 6. Compatibilidade e Manutenibilidade

### 6.1 Versionamento de API
- **Versão Atual:** v1 (parte do endpoint: `/api/v1/transactions`)
- **Política:** Breaking changes → nova versão (`v2`)
- **Suporte:** v1 mantida por 6 meses após v2 launch
- **Deprecation:** HTTP header `Deprecation: true` 3 meses antes de remover

### 6.2 Compatibilidade Backwards
- Adicionar novo campo obrigatório? → Versão nova
- Adicionar campo opcional? → Mesma versão (safe)
- Remover campo? → Versão nova (breaking)

### 6.3 Processo de Deploy
- Blue-Green deployment (zero downtime)
- Canary deployment (5% tráfego primeiro)
- Rollback automático se erro rate > 5%
- Health checks obrigatórios antes de aceitar tráfego

---

## 7. Conformidade

### 7.1 Requisitos Regulatórios (Financeiro)
- **Lei Geral de Proteção de Dados (LGPD):** Direito ao esquecimento, consentimento, transparência
- **Compliance Financeiro:** Auditoria de transações, retenção de 7 anos
- **PCI DSS (se integrar pagamento):** Fora do escopo MVP, documentado para futuro

### 7.2 Conformidade de Dados
- Descrição de erro não expõe detalhes internos (segurança)
- Logs de auditoria imutáveis (hash ou append-only)
- Backup com integridade verificável (checksum)

---

## Resumo de SLAs

| Métrica | Target | Monitorado Por |
|---------|--------|----------------|
| Transactions API Uptime | 99.9% | Health check + Prometheus |
| Consolidation API Uptime | 99.5% | Health check + Prometheus |
| Transactions Latency p95 | ≤ 1000ms | Prometheus histogram |
| Consolidation Latency p95 | ≤ 500ms | Prometheus histogram |
| Consolidation Throughput | ≥ 50 req/s | Load test |
| Isolamento de Falhas | 100% | Test case de falha controlada |
| Cache Hit Rate (Consolidation) | ≥ 50% | Prometheus metrics |
| Error Rate | ≤ 5% | Prometheus counter |
