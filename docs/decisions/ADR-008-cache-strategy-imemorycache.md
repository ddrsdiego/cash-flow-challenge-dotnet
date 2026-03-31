# ADR-008: Cache Strategy — IMemoryCache para MVP, Redis em Produção

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-008 |
| **Status** | Accepted |
| **Data** | 2026-03-31 |
| **Decisores** | Time de Arquitetura |
| **ADRs Relacionadas** | [ADR-002](ADR-002-database-per-service.md) (Database-per-Service), [ADR-006](ADR-006-observability-stack.md) (Observability) |

---

## Contexto e Problema

A **Consolidation API** serve leituras de saldo diário consolidado com requisitos conflitantes:
- **Latência crítica:** < 50ms (SLA 99.5%)
- **Throughput:** ≥ 50 req/s
- **Escalabilidade:** MVP single-instance → Produção multi-instance

Dados consolidados são **leitura intensiva** (consultas de clientes) e **escrita ocasional** (worker atualiza 1x/dia por data).

### Forças em Tensão

| Força | Pressão |
|-------|---------|
| **MVP Simplicidade** | Zero dependências externas (docker-compose local) vs |
| **Escalabilidade Horizontal** | Múltiplas replicas precisam compartilhar cache |
| **Custo Operacional** | IMemoryCache = $0; Redis = infraestrutura |
| **Resiliência** | IMemoryCache perdido ao reiniciar vs Redis persistente |
| **TTL & Invalidation** | IMemoryCache simples vs Redis com pub/sub |

### Problema Central

**Como cachear consolidado para latência < 50ms no MVP, sendo escalável para produção?**

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| MVP deve ser deployável em docker-compose single-machine | Constraint de infraestrutura |
| Consolidação é read-heavy, write-once-per-day | Access pattern específico |
| Latência < 50ms é requisito SLA crítico | RNF de performance |
| Cache não compartilhado por 5 minutos é aceitável single-instance | Pragmatismo MVP |
| Phase 2 tem orçamento para evolução a Redis | Path de mitigação claro |

---

## Opções Consideradas

1. **IMemoryCache (.NET) para MVP → Redis Phase 2** ← **escolhida**
2. Redis desde o MVP
3. Cache em banco (MongoDB queries otimizadas)
4. Sem cache (query MongoDB diretamente)
5. Memcached

---

## Análise Comparativa

### Opção 2: Redis desde o MVP

| Critério | Avaliação |
|----------|-----------|
| MVP Complexidade | ❌ Requer redis container, configuração |
| Escalabilidade | ✅ Imediatamente compartilhado |
| Custo | ⚠️ $0 dev, $$ produção |
| Latência | ✅ 5-10ms (rede) |
| **Veredicto** | **Descartado — overkill para MVP, prematura** |

### Opção 3: Cache em Banco (MongoDB)

| Critério | Avaliação |
|----------|-----------|
| Latência | ❌ 50-200ms (query latency) |
| Simplicidade | ✅ Sem layer de cache |
| **Veredicto** | **Descartado — latência não atende SLA** |

### Opção 4: Sem Cache

| Critério | Avaliação |
|----------|-----------|
| Latência | ❌ 100-200ms |
| SLA | ❌ Não atende < 50ms |
| **Veredicto** | **Descartado — inviável para requisitos** |

### Opção 5: Memcached

| Critério | Avaliação |
|----------|-----------|
| Latência | ✅ < 5ms |
| Escalabilidade | ✅ Cluster-ready |
| Complexidade | ⚠️ Outra dependência de rede |
| **Veredicto** | **Descartado — Redis oferece mais flexibilidade para Phase 2** |

### Opção 1: IMemoryCache MVP → Redis Phase 2 (escolhida)

| Critério | Avaliação |
|----------|-----------|
| MVP Simplicidade | ✅ Built-in .NET, zero config |
| Latência | ✅ < 10ms (in-process) |
| SLA | ✅ Atende < 50ms |
| Trade-off | ⚠️ Não compartilhado entre replicas (5min lag aceitável) |
| Phase 2 Path | ✅ Abstração `IConsolidationCache` prepara evolução |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Decidimos implementar cache com IMemoryCache (.NET) no MVP, com abstração em interface `IConsolidationCache` para permitir migração futura para Redis. Cada replica terá cache local independente com TTL de 5 minutos; consistência eventual é aceitável.**

### Padrão Arquitetural

A Consolidation API implementa padrão de **Cache-Aside**:

1. **GET consolidado:** Consultar cache (IMemoryCache)
   - Cache HIT → Retornar imediatamente (< 10ms)
   - Cache MISS → Query MongoDB → Atualizar cache com TTL 5min

2. **INVALIDAÇÃO:** Quando worker publica `DailyConsolidationUpdatedEvent`
   - Event subscriber invalida cache da data atualizada
   - Próxima requisição refaz query e atualiza cache

3. **TTL:** 5 minutos (balanceia freshness vs hit rate)

### Contrato de Interface

Ver: `docs/architecture/05-cache-implementation.md` — Seção "Interface IConsolidationCache"

---

## Consequências

### Positivas ✅

- **MVP Velocity:** Zero overhead infraestrutura, deploy imediato
- **Latência:** < 10ms cache hit atende SLA rigorosamente
- **Simplicidade:** Built-in .NET, sem dependency management
- **Preparação:** Interface abstrata pronta para evolução

### Negativas — Trade-offs Aceitos ⚠️

- **Escalabilidade horizontal:** Cada replica tem cache independente (lag até 5 min natural)
- **Resiliência:** Cache perdido ao reiniciar container (não crítico, TTL refaz naturalmente)
- **Memory usage:** Cache em RAM aplicação (requer `SizeLimit` em `MemoryCacheOptions`)

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Múltiplas replicas com dados stale até 5min | Média | Médio | Event-driven invalidation; monitorar cache age metrics |
| Memory leak (crescimento não-limitado de cache) | Baixa | Médio | Configurar `SizeLimit` em `MemoryCacheOptions` |
| Produção sem cache compartilhado | Baixa | Alto | Phase 2: migração para Redis Cluster conforme escala |

---

## Phase 2 Migration Path

**Trigger:** Quando arquitetura > 2 replicas em produção

1. Implementar `RedisConsolidationCache : IConsolidationCache`
2. Feature flag para gradual rollout (10% → 50% → 100%)
3. Monitor: latency, hit/miss ratio, memory usage
4. Deprecate MemoryConsolidationCache

Interface permanece invariante — apenas implementação muda.

---

## Referências

- `docs/architecture/05-cache-implementation.md` — Detalhes técnicos de implementação
- [IMemoryCache Microsoft Docs](https://docs.microsoft.com/en-us/aspnet/core/performance/caching/memory)
- [Redis Sentinel](https://redis.io/topics/sentinel) — High availability para Phase 2
- ADR-002: Database-per-Service — contexto de isolamento de dados

---

## Histórico de Revisões

### Revisão 1 (2026-03-31) — Decisão Inicial

Status: Accepted — IMemoryCache para MVP com path claro para Redis Phase 2.
