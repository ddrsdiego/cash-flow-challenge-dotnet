# ADR-007: Defense in Depth JWT — Validação Distribuída de Identidade

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-007 |
| **Status** | Accepted |
| **Data** | 2026-03-26 |
| **Última Revisão** | 2026-03-26 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **Supersedes** | ADR-003 (Propagação de Identidade do Usuário) |
| **ADRs Relacionadas** | [ADR-003](ADR-003-user-context-propagation.md), [ADR-004](ADR-004-api-gateway.md), [ADR-005](ADR-005-authentication-strategy.md), [ADR-006](ADR-006-observability-stack.md) |

---

## Contexto e Problema

O sistema centraliza autenticação via Keycloak (OAuth 2.0 + OpenID Connect). A ADR-003 estabeleceu que o API Gateway extrai o JWT e propaga a identidade via header interno aos serviços downstream, assumindo que os serviços confiam neste header.

Porém, a estratégia original apresenta uma **vulnerabilidade de segurança em cadeia**: se o API Gateway for acidentalmente bypassado (misconfiguration, tráfego direto à rede backend), os serviços aceitam a identidade declarada sem validação — violando a autoridade do Keycloak como fonte de verdade.

### Forças em Tensão

| Força | Pressão | Resolução |
|-------|---------|-----------|
| **Eficiência** | Uma única validação de JWT no Gateway reduz overhead | vs |
| **Resiliência** | Falha em um ponto (Gateway) compromete segurança de toda a cadeia | → Validação distribuída |
| **Simplicidade** | Serviços downstream leem apenas headers | vs |
| **Defense-in-Depth** | Múltiplas camadas reduzem risco de bypass | → Validação independente |
| **Isolamento** | Serviços não devem confiar implicitamente em headers internos | vs |
| **Pragmatismo** | Custo de latência adicional (~5ms por validação) é aceitável para segurança |

### Problema Central

Dado que o MVP pode ter misconfigurações de rede ou acessos diretos não intendidos aos serviços, **como garantir que a identidade é sempre derivada do Keycloak, mesmo se o Gateway for bypassado?**

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Defense-in-depth: múltiplas camadas reduzem risco de falha em ponto único | Princípio de Segurança |
| Serviços não devem depender de headers para decisões de segurança críticas | OWASP — Trusted Data Sources |
| Identidade é derivada exclusivamente do JWT validado | RNF — Autenticação |
| Cada serviço conhece sua própria autoridade (Keycloak) | Autonomia de Serviço |
| Latência adicional (~5ms) é negligenciável comparada ao benefício de segurança | Trade-off aceitável |
| Header X-User-Id é apenas para rastreamento, não para autorização | Separation of Concerns |

---

## Opções Consideradas

1. **Validação distribuída: Gateway + cada serviço valida JWT independentemente** ← **escolhida**
2. Confiar exclusivamente no header interno (ADR-003 original)
3. Header assinado/HMAC — Gateway injeta header + assinatura
4. Extração de JWT exclusivamente em cada serviço, sem passagem de contexto
5. TLS mutual authentication (mTLS) entre serviços como garantia de confiança

---

## Análise Comparativa

### Opção 2: Confiar Exclusivamente no Header Interno (ADR-003)

| Critério | Avaliação |
|----------|-----------|
| Eficiência | ✅ Uma única validação de JWT |
| Resiliência | ❌ Bypass do Gateway = identidade arbitrária nos serviços |
| Defense-in-depth | ❌ Não há camadas redundantes |
| Auditoria | ❌ Se identidade é forjada, impossível rastrear fonte |
| **Veredicto** | **Descartado — vulnerabilidade inaceitável em sistema financeiro** |

---

### Opção 3: Header Assinado com HMAC

Gateway injeta header + HMAC(header + shared-secret). Serviços validam a assinatura.

| Critério | Avaliação |
|----------|-----------|
| Resiliência | ✅ Bypass do Gateway não forja identidade válida |
| Complexity | ❌ Requer shared secret distribuído (risco de key rotation) |
| Auditoria | ⚠️ Identidade validada, mas sem origem no Keycloak |
| **Veredicto** | **Descartado — adiciona complexidade sem validar contra IdP** |

---

### Opção 4: JWT Extraído Exclusivamente em Cada Serviço

Serviços leem JWT do cliente e validam independentemente. Sem header de contexto.

| Critério | Avaliação |
|----------|-----------|
| Resiliência | ✅ Cada serviço valida contra Keycloak |
| Performance | ❌ Latência adicional em CADA serviço |
| Gateway | ❌ Gateway perde função de validador (redundância perdida) |
| Rastreabilidade | ❌ Sem header de contexto para correlação em logs |
| **Veredicto** | **Descartado — perda do benefício de validação única no Gateway** |

---

### Opção 5: mTLS Entre Serviços

Serviços se autenticam mutuamente via certificados. Confiança baseada em TLS.

| Critério | Avaliação |
|----------|-----------|
| Resiliência | ✅ Múltiplas camadas (TLS + JWT) |
| Complexidade | ❌ Requer PKI distribuída, certificado rotation, monitoramento |
| Escopo | ❌ Fora do escopo do MVP (requer Kubernetes/Istio) |
| **Veredicto** | **Descartado — overkill para MVP, considerar em produção** |

---

### Opção 1: Validação Distribuída (escolhida)

**API Gateway valida JWT (1ª camada).** Cada serviço downstream valida JWT independentemente (2ª camada). Header X-User-Id usado apenas para rastreamento.

| Critério | Avaliação |
|----------|-----------|
| Resiliência | ✅ Bypass do Gateway não compromete segurança — serviço ainda valida |
| Eficiência | ✅ Cache de chaves públicas do Keycloak reduz overhead |
| Defense-in-depth | ✅ Múltiplas camadas independentes |
| Auditoria | ✅ Cada validação pode ser rastreada em logs |
| Pragmatismo | ✅ Latência ~5ms aceitável para segurança |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Decidimos implementar validação distribuída de JWT: o API Gateway valida o JWT como primeira camada de defesa; cada serviço downstream valida o JWT independentemente como segunda camada. O header X-User-Id é propagado para rastreamento (logs, correlação OpenTelemetry), mas **nunca** usado para decisões de segurança ou autorização.**

### Fluxo Distribuído de Validação

```
┌──────────────────────────────────────────────────────────────────┐
│              VALIDAÇÃO DISTRIBUÍDA (DEFENSE-IN-DEPTH)            │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Cliente → JWT no header Authorization (Bearer token)         │
│                   ↓                                              │
│  2. API Gateway ┌─ Valida JWT com Keycloak (1ª camada)          │
│                 ├─ Extrai sub (identificador do usuário)         │
│                 ├─ Injeta X-User-Id para rastreamento            │
│                 └─ Passa JWT ao serviço (repassado transparente) │
│                   ↓                                              │
│  3. Transactions.API                                             │
│     ├─ Valida JWT novamente com Keycloak (2ª camada)            │
│     ├─ Extrai sub — associa ao RawRequest                        │
│     ├─ X-User-Id usado para logging/correlação apenas           │
│     └─ Persiste identidade no banco                             │
│                                                                  │
│  ⚠️ Se Gateway bypassado:                                        │
│     → Transactions.API ainda valida JWT                          │
│     → Identidade forjada é rejeitada                             │
│     → Requisição retorna 401 Unauthorized                        │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Regras Arquiteturais

| Regra | Justificativa |
|-------|---------------|
| API Gateway valida JWT com Keycloak (primeira camada) | Checkpoint inicial de segurança |
| Cada serviço valida JWT independentemente (segunda camada) | Resiliência contra bypass |
| X-User-Id **nunca** é usado para autorização ou decisões críticas | Header é decorativo para rastreamento |
| JWT é a única autoridade para decisões de segurança | Derivação exclusiva da identidade |
| Cache de chaves públicas do Keycloak reduz latência em validações | Estratégia de otimização |
| Erros de validação (401) retornam imediatamente sem prosseguir | Fail-fast on security |

---

## Consequências

### Positivas ✅

- **Defense-in-depth operacional:** Múltiplas camadas independentes reduzem risco de falha em ponto único.
- **Bypass-resiliente:** Se Gateway for bypassado, serviços ainda validam contra Keycloak.
- **Auditoria robusta:** Cada ponto de validação pode registrar tentativas de acesso malicioso.
- **Rastreamento correlacionado:** Header X-User-Id disponível para OpenTelemetry e logs, sem comprometer segurança.
- **Base para produção:** Padrão é extensível para mTLS e PKI em evolução futura.

### Negativas — Trade-offs Aceitos ⚠️

- **Latência adicional:** Cada serviço valida JWT (~5ms overhead por serviço); para chain de 3 serviços = ~15ms adicional.
- **Dependência do Keycloak distribuída:** Cada serviço chama Keycloak para validar (mitigado por cache de chaves públicas).
- **Complexidade de teste:** Testes precisam simular validação JWT em múltiplos pontos; helpers de teste requeridos.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Falha de Keycloak afeta todos os serviços simultaneamente | Baixa | Alto | Health checks; fallback para cache local de chaves públicas |
| Chaves públicas do Keycloak não sincronizadas entre serviços | Baixa | Médio | Implementar cache distribuído (Redis) ou sincronização periódica |
| Latência de validação acumulada em pipelines complexos | Média | Baixo | Asynchronous validation; cache warming em startup |

---

## Referências

- [OWASP — Broken Authentication](https://owasp.org/www-project-top-ten/2021/A07_2021-Identification_and_Authentication_Failures/)
- [Defense in Depth — NIST Cybersecurity Framework](https://csrc.nist.gov/publications/detail/sp/800-53/rev-5)
- [OAuth 2.0 Token Introspection — RFC 7662](https://tools.ietf.org/html/rfc7662)
- ADR-003: `docs/decisions/ADR-003-user-context-propagation.md` — decisão anterior (supersedida)
- ADR-004: `docs/decisions/ADR-004-api-gateway.md` — papel do Gateway como ponto de entrada
- ADR-005: `docs/decisions/ADR-005-authentication-strategy.md` — Keycloak como IdP
- ADR-006: `docs/decisions/ADR-006-observability-stack.md` — correlação via X-User-Id

---

## 📜 Histórico de Revisões

### Revisão 1 (2026-03-26) — Elevação de Defense-in-Depth a ADR Independente

**Contexto:** A auditoria de ADR-003 identificou que a estratégia de defense-in-depth JWT (Revisão 2 de ADR-003) era uma mudança arquitetural substancial, não compatível com o princípio de imutabilidade de ADRs aceitos.

**Ação:** Criar ADR-007 como decisão canônica independente, supersedendo ADR-003.

**Benefício:** Clareza arquitetural — decisão vigente sem contradições internas.
