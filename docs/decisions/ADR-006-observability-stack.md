# ADR-006: Estratégia de Observabilidade com OpenTelemetry

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-006 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-27 (revisão formal — ajustes de conformidade Fowler) |
| **Decisores** | Time de Arquitetura |
| **Revisores** | Time de Arquitetura |
| **ADRs Relacionadas** | [ADR-001](ADR-001-async-communication.md), [ADR-004](ADR-004-api-gateway.md), [ADR-007](ADR-007-defense-in-depth-jwt.md) |

---

## Contexto e Problema

O sistema é composto por múltiplos componentes distribuídos que se comunicam de forma síncrona e assíncrona: API Gateway, Transactions API, Consolidation API, Consolidation Worker, RabbitMQ, MongoDB e Redis. Uma requisição do comerciante pode passar por quatro ou mais componentes antes de ser concluída.

Em um sistema distribuído, a ausência de observabilidade torna inviável:

- Diagnosticar a causa raiz de falhas (em qual componente falhou?)
- Rastrear uma requisição específica através de todos os componentes
- Validar que os SLAs definidos estão sendo cumpridos (p95 ≤ 500ms, ≤ 5% de perda)
- Detectar degradação de performance antes que afete os usuários
- Correlacionar um erro reportado pelo comerciante com os logs e traces correspondentes

Observabilidade em sistemas distribuídos requer três sinais complementares — traces, métricas e logs — que devem ser correlacionados via contexto compartilhado (`traceId`). Nenhum pilar isolado é suficiente para diagnóstico efetivo (ver detalhes em `docs/operations/02-monitoring-observability.md`).

### Problema de Fragmentação de Ferramentas

Cada pilar poderia ser instrumentado com bibliotecas específicas de cada vendor (Datadog, New Relic, AWS CloudWatch). Isso cria:

- **Lock-in de vendor:** migrar de Datadog para New Relic exigiria refatoração em todos os serviços
- **APIs inconsistentes:** cada serviço instrumentado de forma diferente, dificultando correlação
- **Custo crescente:** SaaS de observabilidade cobram por volume de dados

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Correlação de sinais — trace, métrica e log com o mesmo contexto (traceId, userId) | RNF — Observabilidade |
| Instrumentação única e portável — sem lock-in de vendor | Princípio de independência de plataforma |
| Rastreamento distribuído — seguir uma requisição por todos os componentes | RNF — Diagnóstico operacional |
| Execução local completa — sem dependência de SaaS externo | Requisito de desenvolvimento e demonstração |
| Alertas sobre SLAs críticos — p95 de latência, volume de DLQ, taxa de erros | RNF — Confiabilidade |
| Auditoria com correlação de identidade — logs com userId + traceId | RNF — Auditoria e compliance |

---

## Opções Consideradas

1. **OpenTelemetry + Jaeger + Prometheus/Grafana + Seq** ← **escolhida**
2. Elastic Stack (ELK/EFK — Elasticsearch, Logstash/Fluentd, Kibana)
3. Datadog (SaaS completo)
4. New Relic (SaaS completo)
5. AWS CloudWatch (SaaS integrado à AWS)
6. Instrumentação nativa por componente (sem camada unificada)

---

## Análise Comparativa

### Opção 3: Datadog

Datadog é uma plataforma SaaS de observabilidade full-stack muito adotada em produção.

| Critério | Avaliação |
|----------|-----------|
| Facilidade de uso | ✅ Excelente — dashboards prontos, correlação automática |
| Execução local | ❌ Agent funciona localmente, mas backend é SaaS |
| Custo | ❌ Alto — cobrado por host + volume de logs + traces |
| Vendor lock-in | ❌ Alto — proprietary SDK e formato de dados |
| Open-source | ❌ Proprietário |
| **Veredicto** | **Descartado — custo e lock-in incompatíveis com MVP demonstrável localmente** |

---

### Opção 4: New Relic

Plataforma SaaS de APM com foco em performance de aplicações, atrativa para times .NET.

| Critério | Avaliação |
|----------|-----------|
| Execução local | ❌ Backend exclusivamente SaaS — impossível rodar offline |
| Custo | ❌ Cobrado por host e volume de dados ingeridos |
| Vendor lock-in | ❌ SDK proprietário e formato de dados não-padrão |
| Open-source | ❌ Proprietário |
| **Veredicto** | **Descartado — custo por volume e impossibilidade de execução offline incompatíveis com o contexto** |

---

### Opção 5: AWS CloudWatch

Serviço gerenciado de observabilidade da AWS, integrado nativamente ao ecossistema Amazon.

| Critério | Avaliação |
|----------|-----------|
| Execução local | ❌ Serviço exclusivo da AWS — sem execução local |
| Vendor lock-in | ❌ Altíssimo — acoplamento total ao ecossistema AWS |
| Custo | ❌ Cobrado por métricas, logs e traces ingeridos |
| Portabilidade | ❌ Migrar para outro cloud exigiria refatoração completa |
| **Veredicto** | **Descartado — lock-in AWS e ausência de execução local inviabilizam demonstração e desenvolvimento** |

---

### Opção 2: Elastic Stack (ELK)

Elasticsearch + Logstash/Fluentd + Kibana é o stack open-source mais consolidado para logs e search.

| Critério | Avaliação |
|----------|-----------|
| Open-source | ✅ (com limitações na licença Elastic) |
| Execução local | ✅ |
| Logs estruturados | ✅ Excelente — busca full-text poderosa |
| Distributed tracing | ⚠️ APM módulo separado; integração com OTLP parcial |
| Métricas | ⚠️ Metricbeat; menos integrado que Prometheus |
| Consumo de recursos | ❌ Elasticsearch é muito resource-intensive (heap mínimo 1-2GB) |
| Complexidade operacional | ❌ Alta — cluster Elasticsearch, shards, índices, ILM policies |
| Curva de aprendizado | ❌ Alta para configuração e operação |
| **Veredicto** | **Descartado — overhead de recursos e complexidade operacional injustificados para o contexto** |

---

### Opção 6: Instrumentação Nativa por Componente

Cada serviço usa a biblioteca nativa de cada ferramenta: `Serilog` diretamente para logs, `prometheus-net` para métricas, `OpenTracing` para traces.

| Critério | Avaliação |
|----------|-----------|
| Simplicidade inicial | ✅ Aparentemente simples por componente |
| Correlação entre sinais | ❌ Cada serviço gera dados em formatos distintos — correlação manual |
| Portabilidade | ❌ Migrar de ferramenta exige refatoração em todos os serviços |
| Padrão de instrumentação | ❌ Cada desenvolvedor instrumenta de forma diferente |
| **Veredicto** | **Descartado — fragmentação impede observabilidade coesa** |

---

### Opção 1: OpenTelemetry + Backends Especializados (escolhida)

OpenTelemetry (OTel) é o projeto da CNCF (Cloud Native Computing Foundation) que padroniza a instrumentação de traces, métricas e logs com uma API única e vendor-neutral. Os dados coletados são exportados para backends especializados.

| Critério | OTel + Jaeger + Prometheus + Seq |
|----------|----------------------------------|
| Vendor lock-in | ✅ Nenhum — instrumentação padronizada (OTLP) |
| Execução local | ✅ Todos os componentes rodam em Docker Compose |
| Distributed tracing | ✅ Jaeger — UI dedicada e madura |
| Métricas | ✅ Prometheus + Grafana — padrão de facto em Kubernetes |
| Logs estruturados | ✅ Seq — busca full-text com filtros estruturados |
| Correlação entre sinais | ✅ traceId propagado automaticamente em todos os sinais |
| .NET 8 suporte | ✅ Suporte nativo via `Microsoft.Extensions.Telemetry` |
| Custo | ✅ Totalmente open-source e self-hosted |
| Migração futura | ✅ Trocar Jaeger por Grafana Tempo ou Tempo Cloud — zero mudança no código |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Adotar OpenTelemetry como camada de instrumentação única e vendor-neutral, com backends especializados para cada pilar: Jaeger para traces distribuídos, Prometheus + Grafana para métricas, e Seq para logs estruturados. O OTel Collector centraliza o recebimento e roteamento de sinais.**

### Arquitetura de Observabilidade

```
┌─────────────────────────────────────────────────────────────────┐
│              SERVIÇOS (instrumentados via OTLP)                 │
│                                                                 │
│  API Gateway   Transactions API   Consolidation API   Worker    │
│       │               │                  │              │       │
│       └───────────────┴──────────────────┴──────────────┘       │
│                              │ OTLP gRPC                        │
└──────────────────────────────┼──────────────────────────────────┘
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│                    OTel Collector                               │
│  (recebe, processa e roteia todos os sinais)                    │
│                                                                 │
│    ┌────────────┐    ┌────────────┐    ┌─────────────┐          │
│    │   Traces   │    │  Métricas  │    │    Logs     │          │
│    └─────┬──────┘    └─────┬──────┘    └──────┬──────┘          │
└──────────┼────────────────┼─────────────────┼───────────────────┘
           ▼                ▼                 ▼
        Jaeger          Prometheus           Seq
    (tracing UI)     (+ Grafana)      (structured logs)
```

### Responsabilidade de Cada Componente

| Componente | Responsabilidade | Sinais |
|------------|-----------------|--------|
| **OTel SDK (nos serviços)** | Instrumentação automática de HTTP, DB, RabbitMQ; propagação de contexto | Traces, métricas, logs |
| **OTel Collector** | Ponto centralizado de recebimento; processamento (enriquecimento, filtragem); roteamento para backends | Todos |
| **Jaeger** | Visualização e busca de traces distribuídos; análise de latência; identificação de gargalos | Traces |
| **Prometheus** | Coleta e armazenamento de métricas time-series; base para alertas | Métricas |
| **Grafana** | Visualização de métricas; dashboards operacionais e de negócio; alertas | Métricas |
| **Seq** | Armazenamento e busca de logs estruturados; alertas baseados em padrões de log | Logs |

### Correlação entre Sinais

O contexto de rastreamento (`traceId`, `spanId`) é propagado automaticamente em todos os sinais, permitindo navegar do log ao trace ao span sem ambiguidade:

```
Log (Seq):      [traceId: abc123] [userId: user-42] Erro ao processar evento
    ↓  traceId: abc123
Trace (Jaeger): abc123 → API Gateway → Transactions API → MongoDB (falhou)
    ↓  traceId: abc123
Métrica (Grafana): spike no contador de erros da Transactions API no mesmo timestamp
```

---

## Consequências

### Positivas ✅

- **Zero lock-in de vendor:** A instrumentação OTLP é agnóstica de backend. Substituir Jaeger por Grafana Tempo, ou Seq por Elastic, não exige nenhuma mudança nos serviços.
- **Correlação nativa entre sinais:** `traceId` propagado em logs, métricas e traces permite diagnosticar qualquer falha com contexto completo.
- **Instrumentação automática:** O SDK do OpenTelemetry para .NET instrumenta automaticamente HTTP, MongoDB, Redis e RabbitMQ — sem código manual nos serviços.
- **Padrão de mercado:** OTel é o projeto de observabilidade mais adotado na CNCF, com suporte nativo de todos os cloud providers e ferramentas de APM.
- **Execução totalmente local:** Todos os componentes funcionam em Docker Compose sem dependência de nuvem.

### Negativas — Trade-offs Aceitos ⚠️

- **Múltiplos componentes adicionais:** O stack de observabilidade adiciona 5 containers (OTel Collector, Jaeger, Prometheus, Grafana, Seq) ao ambiente — aumento de consumo de recursos no ambiente de desenvolvimento.
- **Curva de aprendizado:** Cada ferramenta tem sua própria UI e modelo de dados — Jaeger para traces, Grafana para dashboards, Seq para logs.
- **Dados não correlacionados entre backends:** A navegação entre sinais (de um log para o trace correspondente) requer abertura manual de outra ferramenta. Plataformas SaaS como Datadog fazem essa correlação automaticamente na mesma UI.
- **Retenção limitada no MVP:** Jaeger usa armazenamento in-memory (Badger) — dados de trace não sobrevivem a restart. Em produção, backend persistente (Cassandra, Elasticsearch) é necessário.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| OTel Collector como SPOF de observabilidade | Baixa | Médio | Falha no Collector não afeta o sistema — apenas a visibilidade; restart automático |
| Volume de dados sobrecarrega Prometheus/Seq no MVP | Baixa | Baixo | Retenção curta (7 dias Prometheus, 30 dias Seq); limites de memória configurados |
| Traces incompletos por falha de exportação OTLP | Baixa | Baixo | Perda de observabilidade, não de funcionalidade; buffer no Collector mitiga picos |
| Armazenamento Jaeger perdido em restart (Badger in-memory) | Alta (comportamento normal no MVP) | Baixo | Documentado como limitação do MVP; produção usa backend persistente |
| Acesso não-autorizado a UIs de observabilidade (Grafana, Seq, Jaeger) | Média | Médio | Rede interna isolada no Docker Compose; autenticação habilitada; NetworkPolicy em produção |

### Evolução para Produção

| Aspecto | MVP (Docker Compose) | Produção (Kubernetes) |
|---------|---------------------|-----------------------|
| Backend de traces | Jaeger com Badger (in-memory) | Grafana Tempo ou Jaeger com Cassandra |
| Backend de métricas | Prometheus single-node | Prometheus com remote_write para Thanos/Cortex |
| Backend de logs | Seq | Seq ou Loki (Grafana stack unificado) |
| Alertas | Grafana alerts (básico) | PagerDuty / OpsGenie integrado ao Grafana |
| Sampling de traces | 100% (dev) | Tail-based sampling em produção (reduz volume) |

---

## Referências

- [OpenTelemetry Documentation](https://opentelemetry.io/docs/)
- [CNCF Observability Whitepaper](https://github.com/cncf/tag-observability/blob/main/whitepaper.md)
- [OpenTelemetry .NET SDK](https://github.com/open-telemetry/opentelemetry-dotnet)
- [Jaeger Distributed Tracing](https://www.jaegertracing.io/)
- [Prometheus Documentation](https://prometheus.io/docs/)
- [Seq Structured Logging](https://datalust.co/seq)
- Arquitetura de containers: `docs/architecture/02-container-diagram.md` — Stack de Observabilidade
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md` — Seção 4 (Observabilidade)
- Documentação operacional: `docs/operations/02-monitoring-observability.md`

---

## Histórico de Revisões

### Revisão 1 (2026-03-19)
Decisão inicial de OpenTelemetry como camada de instrumentação única e vendor-neutral, com Jaeger para distributed tracing, Prometheus + Grafana para métricas, e Seq para logs estruturados.

### Revisão 2 (2026-03-25)
Confirmação de alinhamento entre implementação e decisão arquitetural — nenhuma alteração no statement da decisão. Stack de observabilidade instrumentado conforme especificado (OTLP SDK nos serviços, OTel Collector, Jaeger, Prometheus, Grafana, Seq), com correlação de contexto (traceId) propagada em todos os sinais.

### Revisão 3 (2026-03-27)
Revisão formal de conformidade com padrão Fowler e ADR-001 (gold standard). Ajustes aplicados: (1) extração da seção educacional "Três Pilares" para referência inline — redução de verbosidade; (2) desagrupamento da análise de Opções 4 (New Relic) e 5 (AWS CloudWatch) com veredictos individuais; (3) adição de risco de segurança das UIs de observabilidade; (4) inclusão de ADR-007 em ADRs Relacionadas. Nenhuma alteração no statement da decisão.
