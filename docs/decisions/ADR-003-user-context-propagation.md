# ADR-003: Propagação de Identidade do Usuário (User Context Propagation)

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-003 |
| **Status** | Superseded by ADR-007 |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-25 (revisão para implementar defense in depth JWT) |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-001](ADR-001-async-communication.md), [ADR-002](ADR-002-database-per-service.md), [ADR-004](ADR-004-api-gateway.md), [ADR-005](ADR-005-authentication-strategy.md) |

---

## Contexto e Problema

O sistema possui autenticação centralizada via Keycloak (OAuth 2.0 + OpenID Connect). Cada requisição autenticada carrega um JWT com a identidade do usuário, incluindo o identificador único do sujeito autenticado.

O sistema precisa responder a duas questões:

1. **Onde o identificador do usuário deve ser capturado?** — Confiando no que o cliente declara no corpo da requisição, ou extraindo de forma segura do token de autenticação?
2. **Onde essa identidade deve fluir?** — Apenas na camada de entrada, ou propagada até o banco de dados e nos eventos?

### Problema de Segurança Implícito

Aceitar o identificador do usuário como campo do corpo da requisição cria uma **vulnerabilidade de forja de identidade**: um cliente autenticado poderia registrar lançamentos em nome de qualquer outro usuário, simplesmente declarando um identificador diferente no payload. Isso viola o princípio de **não-repúdio** — essencial em sistemas financeiros, onde cada lançamento deve ser irrefutavelmente atribuído ao seu autor.

### Problema de Rastreabilidade e Compliance

Sem o identificador do usuário associado a cada lançamento, o sistema não consegue:
- Auditar quem criou cada transação financeira
- Correlacionar ações de usuários em logs distribuídos
- Atender requisitos de compliance (LGPD exige rastreabilidade de acesso e modificação de dados)
- Evoluir para isolamento de dados por usuário ou comerciante (multi-tenancy)

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Não-repúdio: cada lançamento deve ter autor identificado de forma irrefutável | RNF — Auditoria |
| Identidade não pode ser declarada pelo cliente — deve ser extraída do token | RNF — Autenticação e prevenção de Broken Auth |
| Rastreabilidade em logs estruturados com correlação por usuário | RNF — Observabilidade |
| Conformidade LGPD: saber quem criou e acessou dados pessoais/financeiros | RNF — Compliance |
| Isolamento de dados por usuário está fora do escopo do MVP | Restrições do MVP documentadas |

---

## Opções Consideradas

1. **Extração do JWT no API Gateway + propagação via header seguro** ← **escolhida**
2. Identificador do usuário como campo obrigatório no corpo da requisição
3. Identificador do usuário como campo opcional no corpo (fallback para JWT)
4. Identificador do usuário apenas em logs, sem persistência no banco
5. Extração do JWT diretamente em cada serviço downstream

---

## Análise Comparativa

### Opção 2: Identificador no Corpo da Requisição

```
POST /api/v1/transactions
{ "userId": "outro-usuario", "type": "CREDIT", ... }
```

| Critério | Avaliação |
|----------|-----------|
| Segurança | ❌ Vulnerabilidade de forja de identidade — cliente controla o userId |
| Não-repúdio | ❌ Comprometido — servidor confia em dado declarado pelo cliente |
| **Veredicto** | **Descartado — falha de segurança fundamental em sistema financeiro** |

---

### Opção 3: Campo Opcional com Fallback para JWT

| Critério | Avaliação |
|----------|-----------|
| Segurança | ❌ Comportamento ambíguo — abre brecha se a lógica de fallback falhar |
| Previsibilidade | ❌ Dois caminhos de código para a mesma funcionalidade |
| **Veredicto** | **Descartado — ambiguidade perigosa em sistema financeiro** |

---

### Opção 4: Identificador Apenas em Logs

| Critério | Avaliação |
|----------|-----------|
| Não-repúdio | ❌ Logs podem ser alterados; banco de dados é o registro autoritativo |
| Auditoria | ❌ Impossível buscar "quem criou este lançamento" via consulta estruturada |
| Compliance | ❌ Requisito regulatório não atendido |
| **Veredicto** | **Descartado — não atende requisitos de auditoria** |

---

### Opção 5: Extração do JWT em Cada Serviço Downstream

Cada serviço valida o JWT e extrai o identificador do usuário de forma independente.

| Critério | Avaliação |
|----------|-----------|
| Segurança | ✅ Não depende de input do cliente |
| Redundância | ❌ O API Gateway já valida o JWT — validação duplicada desnecessária |
| Acoplamento | ❌ Cada serviço precisa depender do Keycloak para validação, aumentando latência |
| Escalabilidade | ❌ Cada instância de cada serviço faz chamadas ao Keycloak |
| **Veredicto** | **Descartado — redundante e ineficiente** |

---

### Opção 1: Extração no API Gateway + Propagação via Header (escolhida)

O API Gateway, após validar o JWT com sucesso, extrai o identificador do usuário (claim `sub`) e o propaga para os serviços downstream como um header HTTP seguro interno.

| Critério | Avaliação |
|----------|-----------|
| Segurança | ✅ Cliente nunca controla o identificador do usuário |
| Performance | ✅ JWT validado uma única vez, no Gateway — sem overhead nos serviços downstream |
| Simplicidade | ✅ Serviços downstream leem um header — sem lógica de JWT |
| Rastreabilidade | ✅ Identidade disponível em todos os serviços que necessitam |
| Extensibilidade | ✅ O mesmo padrão pode ser aplicado a outros atributos de contexto no futuro |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Extrair a identidade do usuário do token JWT no API Gateway e propagá-la via header HTTP interno para os serviços downstream. A identidade é persistida em cada lançamento financeiro e propagada nos eventos para fins de auditoria e rastreabilidade.**

### Fluxo de Propagação

```
┌──────────────────────────────────────────────────────────────────┐
│                    FLUXO DE IDENTIDADE                           │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Cliente → JWT no header Authorization (Bearer token)         │
│                   ↓                                              │
│  2. API Gateway → valida JWT com Keycloak                        │
│                 → extrai identificador do usuário (claim sub)    │
│                 → injeta como header interno (X-User-Id)         │
│                   ↓                                              │
│  3. Transactions API → lê o header de identidade                 │
│                      → NÃO aceita userId do corpo da requisição  │
│                      → associa ao lançamento antes de persistir  │
│                   ↓                                              │
│  4. Banco de dados → identidade persistida em cada lançamento    │
│                   ↓                                              │
│  5. Evento (Outbox) → identidade incluída para rastreabilidade   │
│                   ↓                                              │
│  6. Consolidation Worker → recebe a identidade no evento         │
│                           → identidade disponível para auditoria │
│                           → NÃO utilizada no cálculo do saldo    │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Regras Arquiteturais

| Regra | Justificativa |
|-------|---------------|
| A identidade é sempre extraída do JWT pelo Gateway | Única fonte confiável e não forjável |
| O header de identidade é injetado pelo Gateway, nunca pelo cliente | Garantia de não-repúdio |
| Serviços downstream leem o header — não validam o JWT diretamente | Separação de responsabilidade; eficiência |
| A identidade em um lançamento é imutável após a criação | Não-repúdio: não pode ser retroativamente alterada |
| O saldo consolidado não armazena identidade de usuário no MVP | O saldo é do negócio, não de um usuário individual |
| O header de identidade é aceito apenas dentro da rede interna | Header originado externamente não deve ser confiável |

### Por que o Saldo Consolidado não tem Identidade de Usuário

O saldo diário consolida **todos os lançamentos do comerciante**, independentemente de qual usuário os criou. No MVP (single-tenant), o saldo pertence ao negócio, não a um indivíduo.

A identidade em cada lançamento serve exclusivamente à **auditoria** — para saber quem registrou aquela movimentação específica. O saldo total do dia é uma propriedade do negócio, não de um usuário.

A evolução para multi-tenancy introduziria um identificador de comerciante como chave de isolamento — tanto em lançamentos quanto no saldo consolidado. Esse trabalho futuro está documentado como consequência desta decisão.

---

## Consequências

### Positivas ✅

- **Não-repúdio garantido:** Cada lançamento tem autor identificado de forma que não pode ser forjado pelo cliente.
- **Validação de JWT única:** O Gateway valida o token uma vez — serviços downstream não precisam de integração direta com o Keycloak.
- **Rastreabilidade de ponta a ponta:** A identidade flui desde a criação do lançamento até o evento, possibilitando correlação em qualquer consumidor futuro.
- **Base para multi-tenancy:** O campo de identidade em lançamentos estabelece o padrão de propagação de contexto — introduzir um identificador de comerciante no futuro seguirá o mesmo mecanismo.
- **Logs correlacionados:** Identidade disponível em todos os pontos de logging estruturado.

### Negativas — Trade-offs Aceitos ⚠️

- **Acoplamento ao header interno:** Serviços downstream dependem de que o API Gateway injete o header corretamente. Em testes e chamadas internas, o header precisa ser simulado.
- **Header confiável apenas na rede interna:** O header de identidade não deve ser aceito de requisições que bypassa o Gateway. O isolamento de rede é a garantia — não uma validação de assinatura.
- **Identidade não validada contra o IdP downstream:** A Transactions API confia que o Gateway já validou o JWT. Se o Gateway for bypassado, a identidade pode ser arbitrária.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Serviço downstream acessado diretamente, bypassando o Gateway | Baixa | Alto | Rede `backend-net` isolada — serviços não expostos externamente; NetworkPolicy em produção (Kubernetes) |
| Identificador do usuário muda no Keycloak (migração de dados) | Muito Baixa | Médio | Identidade em lançamentos é imutável por design — evento de migração deve ser documentado |
| Header de identidade ausente em chamadas internas de integração | Média | Baixo | Middleware retorna erro explícito se header ausente; helpers de teste devem injetar o header |

---

## Referências

- [OAuth 2.0 JWT Claims — RFC 7519](https://tools.ietf.org/html/rfc7519)
- [OWASP — Broken Object Level Authorization](https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/)
- ADR-001 (Outbox Pattern): `docs/decisions/ADR-001-async-communication.md` — o evento inclui a identidade do usuário
- ADR-002 (Database-per-Service): `docs/decisions/ADR-002-database-per-service.md` — identidade persiste exclusivamente no banco de Transactions
- ADR-004 (API Gateway): `docs/decisions/ADR-004-api-gateway.md` — o Gateway é o ponto de extração e injeção da identidade
- ADR-005 (Authentication Strategy): `docs/decisions/ADR-005-authentication-strategy.md` — o JWT validado é a fonte da identidade
- Requisito funcional: `docs/requirements/01-functional-requirements.md`
- Arquitetura de segurança: `docs/security/01-security-architecture.md` — Controles C6 e C8

---

## 📜 Histórico de Revisões

### Revisão 2 (2026-03-25) — Implementação de Defense in Depth JWT

**Contexto:** A implementação evoluiu para uma estratégia de **defense in depth** onde AMBOS o API Gateway e os serviços downstream validam JWT independentemente, aumentando a resiliência de segurança.

**Mudanças:**

1. **Validação duplicada de JWT (defense in depth):**
   - Gateway valida JWT com Keycloak (primeira camada)
   - Transactions.API valida JWT independentemente (segunda camada)
   - Consolidation.API valida JWT independentemente (segunda camada)
   - **Benefício:** Se o Gateway for bypassado acidentalmente, os serviços não confiam implicitamente

2. **Extraction de identidade varia por serviço:**
   - Transactions.API extrai `sub` do JWT e associa ao RawRequest
   - Consolidation.API extrai `sub` do JWT para `GetDailyConsolidation`
   - Não dependem de header interno X-User-Id para tomar decisões de segurança críticas

3. **Header X-User-Id é apenas para propagação de contexto:**
   - Usado para logging correlacionado
   - Usado para rastreamento em OpenTelemetry
   - **NÃO** usado para decisões de autorização (JWT é a autoridade)

4. **Overhead aceitável:** Validação duplicada de JWT adiciona ~5ms de latência, mas é negligenciável comparado ao benefício de segurança.

**Justificativas:**

- **Defense in depth:** Múltiplas camadas de validação reduzem risco de falha em um ponto único
- **Isolamento:** Serviços não confiam implicitamente em headers de origem interna
- **Auditoria:** Cada serviço tem certeza sobre a identidade, sem deps implícitas

**Trade-offs aceitos:**

- **Latência adicional:** Validação de JWT em cada serviço downstream (~5ms)
- **Dependência indireta do Keycloak:** Serviços downstream chamam Keycloak para validar JWT (com cache de chaves públicas)

**Impacto em outros ADRs:**

- ADR-001: Sem impacto — eventos continuam carregando identidade
- ADR-002: Sem impacto — RawRequests e Transactions carregam userId
- ADR-004: Gateway continua como validador primeira camada
- ADR-005: Keycloak continua como IdP, mas agora chamado por serviços downstream também
- ADR-006: OpenTelemetry continua correlacionando por userId do header e/ou JWT

### Revisão 3 (2026-03-26) — Elevação de Defense in Depth a ADR Independente

**Contexto:** A auditoria de conformidade com Martin Fowler pattern identificou que a Revisão 2 continha uma **mudança de decisão arquitetural** (não apenas simplificação de formato), que viola o princípio de imutabilidade de ADR aceitos.

**Decisão:** 
- Marcar ADR-003 como "Superseded by ADR-007"
- Criar ADR-007 (Defense in Depth JWT) como decisão canônica independente
- ADR-003 preservado como registro histórico da evolução arquitetural

**Justificativa:**
- Imutabilidade: ADRs aceitos não devem ter decisões reescritas
- Clareza: Decisão vigente está em um único documento sem contradições internas
- Rastreabilidade: Fica registrado o momento em que a arquitetura evoluiu