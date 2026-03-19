# Entendimento da Tarefa — Sessão 01

## Referência
Mini-PRD: `docs/plano-implementacao.md`

## Contexto
Desafio técnico de Arquiteto de Soluções. O projeto está em estágio inicial:
- ✅ Infraestrutura (docker-compose) já configurada com MongoDB, Redis, RabbitMQ, Keycloak, OTel
- ✅ Pastas docs/ criadas com estrutura planejada
- ❌ Documentação arquitetural vazia
- ❌ Código-fonte ainda não iniciado

## Tarefa
Produzir os três primeiros documentos de arquitetura que estabelecem a fundação do projeto:
1. `docs/requirements/01-functional-requirements.md` — O que o sistema deve fazer
2. `docs/requirements/02-non-functional-requirements.md` — Como o sistema deve se comportar
3. `docs/architecture/05-domain-mapping.md` — Como o problema se divide em domínios

## Objetivo
Estabelecer a fundação documental que sustenta as fases subsequentes:
- **Clareza dos requisitos funcionais** — User stories, casos de uso, regras de negócio
- **Definição formal de SLAs** — Throughput 50 req/s ≤5% perda, disponibilidade 99,9%
- **Mapeamento de domínios** — Identificar bounded contexts e linguagem ubíqua

## Escopo

### IN SCOPE
✅ Requisitos funcionais:
  - Serviço de Lançamentos: criar débitos e créditos no fluxo de caixa
  - Serviço de Consolidado: gerar relatório de saldo diário consolidado

✅ Requisitos não funcionais com métricas:
  - Throughput: 50 req/s (consolidado)
  - Taxa de perda aceitável: ≤ 5%
  - Disponibilidade: diferenciada por serviço
  - Isolamento de falhas: Lançamentos não afetado por falha do Consolidado

✅ Domain mapping:
  - 2 bounded contexts principais
  - Domain events e fluxo de informação
  - Linguagem ubíqua do domínio financeiro

### OUT OF SCOPE
❌ Diagramas C4 (Context, Container, Components) — Sessão 02+
❌ Architecture Decision Records (ADRs) — Sessão 04
❌ Código-fonte e implementação — Fase 4
❌ Testes e validação técnica — Fase 5

## Entregáveis

| Arquivo | Propósito | Estimativa |
|---------|-----------|-----------|
| `.arquitetura/sessao-01/entendimento.md` | Este documento | ✅ |
| `docs/requirements/01-functional-requirements.md` | User stories, regras de negócio, casos de uso | 1h |
| `docs/requirements/02-non-functional-requirements.md` | SLAs, métricas, segurança, observabilidade | 1h |
| `docs/architecture/05-domain-mapping.md` | Bounded contexts, domain events, glossário | 1h |

## Critérios de Aceite

### Requisitos Funcionais
- [ ] Cobre todos os casos de uso (lançamento, consulta de consolidado)
- [ ] Define as regras de negócio (validações, cálculo de saldo)
- [ ] User stories em formato padrão: "Como X, quero Y, para Z"
- [ ] Fluxos alternativos e de exceção documentados

### Requisitos Não Funcionais
- [ ] Métricas são mensuráveis (não apenas "deve ser rápido")
- [ ] SLAs diferenciados por serviço (Transactions vs Consolidation)
- [ ] Throughput e latência p95/p99 especificados
- [ ] Resiliência descreve isolamento de falhas
- [ ] Segurança aborda autenticação, autorização, proteção de dados

### Domain Mapping
- [ ] 2 bounded contexts claramente identificados
- [ ] Domain events documentados (o que muda em cada contexto)
- [ ] Linguagem ubíqua com glossário
- [ ] Context Map mostra relação entre contextos (Published Language pattern)
- [ ] Capacidades de negócio definidas por contexto

## Notas Importantes

1. **Requisitos definem o problema, Domínio explica como resolvê-lo**
   - Requisitos: "Sistema deve processar 50 req/s"
   - Domínio: "Consolidation context é responsável por agregar dados para saldo diário"

2. **Isolamento de falhas é um requisito crítico**
   - O desafio explica: "Lançamentos NÃO pode falhar se Consolidado falhar"
   - Isso impacta toda a arquitetura — comunicação assíncrona é obrigatória

3. **Throughput e latência são diferentes**
   - Throughput: 50 req/s (taxa de processamento)
   - Latência: quanto tempo leva para responder (p95, p99)
   - Ambos importam para o sistema

---

**Status:** ✅ Documento de entendimento criado e aprovado. Prosseguir com criação dos 3 arquivos de documentação.
