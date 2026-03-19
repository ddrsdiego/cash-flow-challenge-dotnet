# Entendimento da Tarefa — Sessão 02

## Referência
Sessão anterior: `.arquitetura/sessao-01/entendimento.md`  
Plano: `docs/plano-implementacao.md` (FASE 1, items 1.3 e 1.4)

---

## Contexto

Sessão 01 concluída com sucesso:
- ✅ `docs/requirements/01-functional-requirements.md` — Casos de uso, endpoints, regras de negócio
- ✅ `docs/requirements/02-non-functional-requirements.md` — SLAs, latência, segurança, observabilidade
- ✅ `docs/architecture/05-domain-mapping.md` — 2 bounded contexts, domain events, context map

**Estado do projeto:**
- ✅ Infraestrutura (docker-compose.yml) — Completa e alinhada
- ✅ Documentação base — Requisitos e domain mapping validados
- ❌ Código-fonte — Ainda não iniciado
- ❌ Diagramas arquiteturais — Necessários para comunicar decisões

---

## Tarefa

Produzir os dois primeiros diagramas C4 que comunicam a arquitetura alvo:

| # | Arquivo | Conteúdo | Padrão |
|---|---------|---------|--------|
| 1 | `docs/architecture/01-context-diagram.md` | Visão de alto nível do sistema | C4 Level 1 |
| 2 | `docs/architecture/02-container-diagram.md` | Containers, responsabilidades e comunicações | C4 Level 2 |

---

## Objetivo

Demonstrar maturidade arquitetural através de diagramas estruturados que deixem claras:
- ✅ Responsabilidades de cada componente
- ✅ Fluxo de dados entre sistemas
- ✅ Dependências e pontos de integração
- ✅ Limites de rede e isolamento

**Critério de aceite do desafio:**
> "Diagramas que não comunicarem claramente a arquitetura poderão ser considerados incompletos."

---

## Escopo

### IN SCOPE ✅

**C4 Level 1 — Context Diagram:**
- Ator: **Comerciante** (usuário do sistema)
- Sistema: **CashFlow System** (tratado como caixa preta)
- Sistema externo: **Keycloak** (Identity Provider)
- Interações: 
  - Comerciante autenticado pelo Keycloak
  - Comerciante registra lançamentos (débitos/créditos)
  - Comerciante consulta saldo diário consolidado

**C4 Level 2 — Container Diagram:**
- **4 containers de aplicação:**
  - API Gateway (YARP) — ponto de entrada
  - Transactions API — registra lançamentos
  - Consolidation API — consulta saldo
  - Consolidation Worker — processamento assíncrono

- **3 data stores:**
  - MongoDB — 2 databases (transactions_db, consolidation_db)
  - Redis — cache para consolidação
  - RabbitMQ — broker de eventos

- **1 Identity Provider:**
  - Keycloak — autenticação OAuth2/OIDC

- **Stack de observabilidade:**
  - OTel Collector, Jaeger, Prometheus/Grafana, Seq
  - (mostrado como "externa" ou "auxiliar")

- **Limites de rede:**
  - frontend-net (acesso externo)
  - backend-net (serviços internos)
  - monitoring-net (observabilidade)

- **Fluxo de dados:**
  - Transactions API → RabbitMQ → Worker → Redis (invalidação)
  - Consolidation API → Redis (cache-first) / MongoDB (fallback)
  - Todas as requisições → API Gateway → autenticação
  - Todos os serviços → OTel Collector

### OUT OF SCOPE ❌

- ❌ C4 Level 3 (Component Diagrams) — Sessão 03
- ❌ Código-fonte — Sessão 04+
- ❌ Testes — Sessão 05
- ❌ Diagramas de sequência detalhados — Sessão 03
- ❌ ADRs (justificativas de decisões) — Sessão 04

---

## Stack Arquitetural (Confirmado)

| Componente | Tecnologia | Razão |
|-----------|-----------|-------|
| APIs | .NET 8 Minimal APIs | Performance, AOT-ready |
| Gateway | YARP | Nativo .NET, extensível |
| DB | MongoDB 7.0 | Database-per-service |
| Cache | Redis 7.2 | Cache-first para consolidação |
| Broker | RabbitMQ 3.13 | Event-driven, DLQ |
| Auth | Keycloak 24.0 | OAuth2/OIDC |
| Observability | OTel + Jaeger + Prometheus + Seq | Tracing distribuído + metrics + logs |

---

## Critérios de Aceite

### C4 Level 1 — Context Diagram
- [ ] Comerciante identificado como ator externo
- [ ] CashFlow System como caixa preta (sem detalhes internos)
- [ ] Keycloak como sistema externo
- [ ] 3 relacionamentos claros (auth, criar lançamento, consultar saldo)
- [ ] Diagrama renderiza no GitHub (Mermaid)

### C4 Level 2 — Container Diagram
- [ ] 4 containers de aplicação identificados
- [ ] 3 data stores identificados
- [ ] 1 identity provider
- [ ] Stack de observabilidade representado
- [ ] 3 limites de rede (frontend, backend, monitoring)
- [ ] Fluxo de dados comunicado: request → gateway → services → data stores
- [ ] Fluxo de eventos comunicado: transactions → rabbit → worker → redis
- [ ] Cache-first pattern visível na consolidação
- [ ] Todos os containers com responsabilidades claras
- [ ] Diagrama renderiza no GitHub (Mermaid)

---

## Formato dos Diagramas

Utilizaremos **Mermaid C4 Diagram** dentro de arquivos Markdown:

**Vantagens:**
- ✅ Renderiza nativamente no GitHub
- ✅ Versionável como código
- ✅ Legível mesmo sem renderização
- ✅ Fácil de manter e atualizar

**Estrutura do arquivo:**
```markdown
# 01 — Context Diagram

## Visão Geral
[Descrição textual]

## Diagrama
\`\`\`mermaid
C4Context
  [diagram code]
\`\`\`

## Descrição dos Elementos
[Detalhamento de cada ator/sistema]
```

---

## Entregáveis Esperados

```
docs/architecture/
├── 01-context-diagram.md       # C4 Level 1
├── 02-container-diagram.md     # C4 Level 2
├── 03-component-transactions.md    (próxima sessão)
└── ...
```

**Padrão de documentação:**
- Título claro (C4 Level X)
- Visão geral textual (o que o diagrama comunica)
- Diagrama Mermaid renderizável
- Legenda descritiva de cada elemento
- Fluxo de dados explicado

---

## Princípios de Desenho

1. **Clareza acima de detalhes**
   - C4 Level 1 = ator + sistema como caixa preta
   - C4 Level 2 = containers com responsabilidades
   - Sem poluição visual

2. **Responsabilidades bem definidas**
   - Cada container tem um propósito único
   - Labels deixam clara a função

3. **Fluxo de dados visível**
   - Setas com labels indicando o tipo de comunicação
   - Padrões (síncrono, assíncrono, cache) diferenciados

4. **Limites de rede explícitos**
   - Agrupamentos (redes) mostram isolamento
   - Segurança refletida na topologia

5. **Rastreabilidade aos requisitos**
   - C4 diagrama complementa documentação textual
   - Cada componente alinhado ao domain mapping

---

**Status:** ✅ Entendimento validado. Pronto para implementação.

---

*Próximas ações após esta sessão:*
1. ✅ Criar 01-context-diagram.md
2. ✅ Criar 02-container-diagram.md
3. → Sessão 03: C4 Components + Fluxos de sequência
