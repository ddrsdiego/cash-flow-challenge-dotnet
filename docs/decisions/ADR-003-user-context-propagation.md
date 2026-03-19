# ADR-003: Propagação de Identidade do Usuário (User Context Propagation)

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-003 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-001](ADR-001-async-communication.md), [ADR-002](ADR-002-database-per-service.md) |

---

## Contexto e Problema

O sistema possui autenticação centralizada via **Keycloak (OAuth 2.0 + OpenID Connect)**. Cada requisição autenticada carrega um JWT com informações do usuário, incluindo o `sub` (subject) — identificador único do usuário no Identity Provider.

O sistema precisa responder a duas perguntas:

1. **Onde o `userId` deve ser capturado?** — No cliente (body da requisição) ou extraído do JWT pelo servidor?
2. **Onde o `userId` deve fluir?** — Apenas na API? No banco de dados? Nos eventos publicados?

### Problema de Segurança Implícito

Aceitar `userId` como campo do body da requisição criaria uma **vulnerabilidade de elevação de privilégio**:

```
POST /api/v1/transactions
{
  "userId": "outro-usuario",   ← cliente forjando identidade
  "type": "CREDIT",
  "amount": 500.00
}
```

Um usuário autenticado poderia criar lançamentos em nome de qualquer outro usuário. Isso viola o princípio básico de **non-repudiation** (não-repúdio) em sistemas financeiros.

### Problema de Rastreabilidade

O modelo de dados original da `Transaction` não incluía `userId`. Isso impossibilita:
- Auditoria de quem criou cada lançamento
- Rastreabilidade em logs distribuídos (correlação `userId → traceId → lançamento`)
- Evolução futura para multi-tenancy (isolamento por usuário/comerciante)
- Compliance financeiro (LGPD requer saber quem acessou/modificou dados)

### Decisão Arquitetural Necessária

Definir formalmente:
- **Como** o `userId` é obtido (extração do JWT, não input do cliente)
- **Onde** o `userId` é propagado (Transaction, evento, logs)
- **O que não muda** (DailyConsolidation permanece sem `userId` no MVP)
- **Como** o `userId` flui entre API Gateway e serviços downstream

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Non-repudiation: cada lançamento deve ter autor identificado | RNF 3.6 — Auditoria |
| userId não pode ser forjado pelo cliente | RNF 3.1 — Autenticação, RNF 3.5 — Broken Auth |
| Rastreabilidade em logs estruturados | RNF 4.3 — Logs (`userId` já referenciado no formato de log) |
| Conformidade LGPD: saber quem criou dados | RNF 7.1 — Conformidade |
| Isolamento de dados por usuário é fora do escopo MVP | `docs/requirements/01-functional-requirements.md` — Restrições |

---

## Opções Consideradas

1. **Extração do JWT no API Gateway + propagação via header** ← **escolhida**
2. `userId` como campo obrigatório no body da requisição
3. `userId` como campo opcional no body (fallback para JWT)
4. `userId` não armazenado (apenas em logs)
5. Extração do JWT diretamente em cada serviço downstream

---

## Análise Comparativa

### Opção 2: userId como campo obrigatório no body

```json
POST /api/v1/transactions
{
  "userId": "user-123",
  "type": "CREDIT",
  ...
}
```

| Critério | Avaliação |
|----------|-----------|
| Segurança | ❌ Vulnerabilidade de forja de identidade |
| Non-repudiation | ❌ Comprometido — servidor confia no cliente |
| Experiência do desenvolvedor | ⚠️ Mais complexo — cliente precisa conhecer seu próprio userId |
| **Veredicto** | **Descartado — falha de segurança fundamental** |

---

### Opção 3: userId opcional no body (fallback para JWT)

```json
// Se não informado → usa JWT. Se informado → usa o body?
```

| Critério | Avaliação |
|----------|-----------|
| Segurança | ❌ Comportamento ambíguo — abre brecha se validação falhar |
| Previsibilidade | ❌ Dois caminhos de código para mesma funcionalidade |
| **Veredicto** | **Descartado — ambiguidade perigosa em sistema financeiro** |

---

### Opção 4: userId não armazenado (apenas em logs)

| Critério | Avaliação |
|----------|-----------|
| Non-repudiation | ❌ Logs podem ser alterados; banco de dados é mais confiável |
| Auditoria | ❌ Não é possível buscar "quem criou este lançamento" via query |
| Compliance | ❌ Requisito regulatório não atendido |
| **Veredicto** | **Descartado — não atende requisitos de auditoria** |

---

### Opção 5: Extração do JWT diretamente em cada serviço downstream

```
Transactions API extrai JWT → valida com Keycloak → obtém userId
```

| Critério | Avaliação |
|----------|-----------|
| Segurança | ✅ Não depende de input do cliente |
| Acoplamento | ❌ Cada serviço precisa validar JWT com Keycloak (latência adicional) |
| Redundância | ❌ API Gateway já valida JWT — dupla validação desnecessária |
| Escalabilidade | ❌ Cada instância de cada serviço faz chamadas ao Keycloak |
| **Veredicto** | **Descartado — redundante e ineficiente** |

---

### Opção 1: Extração no API Gateway + propagação via header (escolhida)

O API Gateway já valida o JWT (via Keycloak). Após validação bem-sucedida, extrai o claim `sub` e propaga para os serviços downstream como header HTTP seguro.

```
Cliente → API Gateway → [valida JWT, extrai sub] → header X-User-Id: "user-123"
                                                  → Transactions API (lê X-User-Id)
```

| Critério | Avaliação |
|----------|-----------|
| Segurança | ✅ Cliente nunca controla userId |
| Performance | ✅ JWT validado uma vez (no Gateway), não em cada serviço |
| Simplicidade | ✅ Serviços downstream apenas leem um header — sem lógica de JWT |
| Rastreabilidade | ✅ userId disponível em todos os serviços que precisam |
| Extensibilidade | ✅ Outros headers podem ser propagados da mesma forma (e.g., `X-Tenant-Id` futuro) |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Extrair `userId` do claim `sub` do JWT no API Gateway e propagá-lo via header HTTP `X-User-Id` para os serviços downstream. O `userId` é persistido em cada `Transaction` e incluído no evento `TransactionCreated` para fins de auditoria e rastreabilidade.**

### Fluxo de Propagação

```
┌──────────────────────────────────────────────────────────────────┐
│                    FLUXO DE userId                               │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. Cliente → JWT (Authorization: Bearer token)                 │
│              ↓                                                   │
│  2. API Gateway → valida JWT com Keycloak                       │
│                 → extrai claim: sub = "user-123"                │
│                 → injeta header: X-User-Id: "user-123"          │
│              ↓                                                   │
│  3. Transactions API → lê X-User-Id do header                  │
│                      → NÃO aceita userId do body                │
│                      → associa userId ao lançamento             │
│              ↓                                                   │
│  4. MongoDB → persiste Transaction.userId = "user-123"         │
│              ↓                                                   │
│  5. Evento TransactionCreated → data.userId = "user-123"       │
│              ↓                                                   │
│  6. Consolidation Worker → recebe userId no evento              │
│                           → userId NÃO é usado no cálculo      │
│                           → disponível para audit trail futuro  │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Regras de Implementação

| Regra | Detalhe |
|-------|---------|
| **Fonte do userId** | Claim `sub` do JWT (não body, não query string) |
| **Propagação** | Header `X-User-Id` injetado pelo API Gateway |
| **Serviços downstream** | Leem `X-User-Id` — não validam o JWT diretamente |
| **Imutabilidade** | `userId` nunca é alterado após criação da Transaction |
| **Consolidação** | `DailyConsolidation` não armazena `userId` (saldo é global no MVP) |
| **Evento** | `TransactionCreated.data.userId` propagado para rastreabilidade |

### Estrutura do Evento com userId

```json
{
  "eventId": "{{uuid}}",
  "eventType": "TransactionCreated",
  "version": "1.0",
  "aggregateId": "{{transaction-id}}",
  "aggregateType": "Transaction",
  "timestamp": "2024-03-15T15:30:00Z",
  "idempotencyKey": "{{uuid}}",
  "data": {
    "transactionId": "507f1f77bcf86cd799439011",
    "userId": "user-123",
    "type": "CREDIT",
    "amount": 500.00,
    "description": "Venda do dia",
    "category": "Sales",
    "date": "2024-03-15"
  }
}
```

### Schema MongoDB da Collection transactions

```
transactions_db.transactions
├── _id (ObjectId)
├── userId (String — claim sub do JWT)
├── type (DEBIT|CREDIT)
├── amount (Decimal128)
├── description (String)
├── category (String)
├── date (Date)
├── createdAt (Timestamp)
└── updatedAt (Timestamp)

Índices:
  - date + type (composto) — queries por período
  - userId (simples)        — queries de auditoria por usuário
```

### Por que DailyConsolidation não tem userId

O saldo diário consolida **todas as transações do comerciante**, independentemente de qual usuário (funcionário, gerente, dono) as criou. No MVP (single-tenant), há apenas um comerciante — o saldo é do negócio, não de um usuário individual.

```
Cenário real:
  Gerente A cria lançamento de R$500 de venda
  Caixa B cria lançamento de R$150 de despesa
  → Saldo do dia: R$350 (soma de todos os lançamentos do negócio)
  → userId de cada lançamento é para AUDITORIA
  → O saldo não pertence ao Gerente A nem ao Caixa B — pertence ao negócio
```

A evolução natural para multi-tenancy introduziria um `merchantId` como chave de isolamento — neste ponto, tanto `DailyConsolidation` quanto `Transaction` precisariam de `merchantId`. Esta evolução é documentada como trabalho futuro.

---

## Consequências

### Positivas ✅

- **Non-repudiation garantido:** Cada lançamento tem autor identificado de forma que não pode ser forjado pelo cliente.
- **Validação de JWT única:** API Gateway valida o JWT uma vez — serviços downstream não precisam de dependência com Keycloak.
- **Rastreabilidade de ponta a ponta:** `userId` flui desde a criação da Transaction até o evento, possibilitando correlação em qualquer consumidor futuro.
- **Base para multi-tenancy:** O campo `userId` estabelece o padrão de propagação de identidade — introduzir `merchantId` no futuro seguirá o mesmo padrão.
- **Logs correlacionados:** `userId` disponível em todos os contextos de log estruturado (já referenciado no formato de log em `02-non-functional-requirements.md`).

### Negativas — Trade-offs Aceitos ⚠️

- **Acoplamento ao header `X-User-Id`:** Serviços downstream dependem de que o API Gateway injete este header corretamente. Se o Gateway for bypassado (em testes, por exemplo), o header precisa ser injetado manualmente.
- **Header confiável apenas dentro da rede interna:** `X-User-Id` não deve ser aceito de fora da rede do API Gateway. A configuração de rede (`backend-net` isolado) garante que requisições externas não passam direto para os serviços.
- **userId não valida existência:** A Transactions API não verifica se `userId` existe no Keycloak — confia que o Gateway já validou o JWT. Se o JWT foi validado, o usuário existe.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Serviço downstream bypassando o Gateway (acesso direto) | Baixa | Alto (userId pode ser injetado arbitrariamente) | Rede `backend-net` isolada — serviços não expostos diretamente; adicionar validação de origem em produção |
| Claim `sub` muda no Keycloak (e.g., migração de usuário) | Muito Baixa | Médio (historical records com userId antigo) | Documentar como evento de migração; userId em Transaction é imutável por design |
| Header `X-User-Id` ausente em chamadas internas (e.g., testes) | Média | Baixo (erro de validação explícito) | Middleware retorna 400 se header ausente; testes usam helpers para injetar header |

---

## Referências

- [OAuth 2.0 JWT Claims — RFC 7519](https://tools.ietf.org/html/rfc7519)
- [OWASP — Broken Object Level Authorization](https://owasp.org/API-Security/editions/2023/en/0xa1-broken-object-level-authorization/)
- ADR-001 (Outbox Pattern): `docs/decisions/ADR-001-async-communication.md` — evento `TransactionCreated` agora inclui `userId`
- ADR-002 (Database-per-Service): `docs/decisions/ADR-002-database-per-service.md` — schema de `transactions` agora inclui `userId`
- Requisito funcional: `docs/requirements/01-functional-requirements.md` — RN-08
- Domain Mapping: `docs/architecture/05-domain-mapping.md` — modelo Transaction e evento TransactionCreated
- Component Diagram: `docs/architecture/03-component-transactions.md` — domain aggregate Transaction
- Container Diagram: `docs/architecture/02-container-diagram.md` — schema MongoDB
