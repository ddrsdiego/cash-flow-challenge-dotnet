# 02 — Authentication & Authorization

## Visão Geral

Este documento detalha as estratégias de **autenticação** (quem é você?) e **autorização** (o que você pode fazer?) adotadas pelo CashFlow System.

O modelo adotado combina:
- **OAuth 2.0 + OpenID Connect** para autenticação e emissão de tokens
- **JWT (JSON Web Token)** como formato de token portador
- **RBAC (Role-Based Access Control)** para controle de acesso granular
- **Keycloak** como Identity Provider (IdP) centralizado

---

## 1. Autenticação — OAuth 2.0 + OpenID Connect

### 1.1 Fluxo de Autenticação (Resource Owner Password Flow — MVP)

> **Nota:** O Resource Owner Password Flow é adotado no MVP por simplicidade de integração via REST client. Em produção com frontend, o fluxo recomendado é o **Authorization Code Flow com PKCE**.

```
┌──────────────────┐                    ┌─────────────────┐
│   Comerciante    │                    │    Keycloak     │
│  (REST client)   │                    │  (Identity IdP)  │
└────────┬─────────┘                    └────────┬────────┘
         │                                       │
         │  POST /realms/cashflow/protocol/      │
         │       openid-connect/token            │
         │  {username, password, client_id,      │
         │   grant_type: password}               │
         ├──────────────────────────────────────►│
         │                                       │
         │       200 OK                          │
         │  {access_token, refresh_token,        │
         │   expires_in: 3600}                   │
         │◄──────────────────────────────────────┤
         │                                       │
         │  GET /api/v1/consolidation/daily      │
         │  Authorization: Bearer {access_token} │
         ├──────────────────────────────────────►│  (API Gateway valida)
         │                                       │
```

### 1.2 Fluxo de Autenticação (Authorization Code Flow com PKCE — Produção com Frontend)

```
┌──────────────────┐     ┌───────────────┐     ┌─────────────────┐
│   Comerciante    │     │  API Gateway  │     │    Keycloak     │
│   (Frontend)     │     │     (YARP)    │     │  (Identity IdP)  │
└────────┬─────────┘     └───────┬───────┘     └────────┬────────┘
         │                       │                       │
         │  1. Acessa sistema    │                       │
         ├──────────────────────►│                       │
         │                       │                       │
         │  2. Redireciona para  │  3. Authorization     │
         │     Keycloak          │     Request           │
         │◄──────────────────────┤──────────────────────►│
         │                       │                       │
         │  4. Login UI Keycloak │                       │
         │◄─────────────────────────────────────────────-│
         │                       │                       │
         │  5. Credenciais       │                       │
         ├────────────────────────────────────────────►  │
         │                       │                       │
         │  6. Authorization Code│                       │
         │◄──────────────────────────────────────────────┤
         │                       │                       │
         │  7. Code + code_verifier                      │
         │  POST /token           │                       │
         ├──────────────────────────────────────────────►│
         │                       │                       │
         │  8. access_token + refresh_token              │
         │◄──────────────────────────────────────────────┤
         │                       │                       │
         │  9. Request com Bearer token                  │
         ├──────────────────────►│                       │
         │                       │  10. Valida token    │
         │                       ├──────────────────────►│
         │                       │  11. Introspection   │
         │                       │◄──────────────────────┤
         │                       │                       │
         │  12. Response         │                       │
         │◄──────────────────────┤                       │
```

### 1.3 Fluxo de Refresh de Token

```
Quando access_token expira (após 1h):

Cliente → POST /realms/cashflow/protocol/openid-connect/token
  {grant_type: refresh_token, refresh_token: {token}, client_id: cashflow-api}

Keycloak → 200 OK
  {access_token: {novo}, refresh_token: {rotacionado}, expires_in: 3600}
```

**Refresh token rotation:** A cada uso do refresh_token, um novo refresh_token é emitido e o anterior é invalidado. Isso mitiga roubo de refresh tokens.

---

## 2. JWT — Estrutura e Validação

### 2.1 Estrutura do JWT

**Header:**
```json
{
  "alg": "RS256",
  "typ": "JWT",
  "kid": "cashflow-key-2024"
}
```

**Payload (Claims):**
```json
{
  "iss": "http://keycloak:8080/realms/cashflow",
  "sub": "550e8400-e29b-41d4-a716-446655440000",
  "aud": "cashflow-api",
  "exp": 1710518400,
  "iat": 1710514800,
  "jti": "unique-token-id",
  "typ": "Bearer",
  "azp": "cashflow-api",
  "session_state": "session-uuid",
  "realm_access": {
    "roles": ["transactions:read", "transactions:write", "consolidation:read"]
  },
  "resource_access": {
    "cashflow-api": {
      "roles": ["user"]
    }
  },
  "preferred_username": "comerciante@exemplo.com",
  "email": "comerciante@exemplo.com",
  "name": "João Comerciante"
}
```

**Signature:** RS256 — assinado com chave privada do Keycloak; validado com chave pública.

### 2.2 Claims Utilizados pelo Sistema

| Claim | Uso |
|-------|-----|
| `sub` | Extraído pelo Gateway como `userId`; persistido em cada Transaction |
| `exp` | Verificado pelo Gateway — token expirado = 401 |
| `iss` | Verificado pelo Gateway — emissor deve ser o Keycloak configurado |
| `aud` | Verificado pelo Gateway — audiência deve ser `cashflow-api` |
| `realm_access.roles` | Verificado pelo Gateway para RBAC |

### 2.3 Validação do JWT no API Gateway

```
Algoritmo de validação:
1. Extrair header Authorization → Bearer {token}
2. Decodificar header JWT → obter alg e kid
3. Buscar chave pública do Keycloak JWKS endpoint
   (GET /realms/cashflow/protocol/openid-connect/certs)
4. Verificar assinatura RS256 com chave pública
5. Verificar claim iss == "http://keycloak:8080/realms/cashflow"
6. Verificar claim aud == "cashflow-api"
7. Verificar claim exp > now() (token não expirado)
8. Se tudo válido → extrair sub → injetar X-User-Id header
9. Se qualquer verificação falhar → 401 Unauthorized
```

**Caching da chave pública:** O Gateway cacheia as chaves JWKS por 1 hora para evitar chamada ao Keycloak a cada requisição. Se `kid` não encontrado no cache, força re-busca (suporte a rotação de chaves).

### 2.4 Por que RS256?

| | RS256 (RSA + SHA-256) | HS256 (HMAC + SHA-256) |
|--|----------------------|------------------------|
| Tipo | Assimétrico | Simétrico |
| Chave de verificação | Pública (pode ser compartilhada) | Segredo compartilhado |
| Segurança | ✅ Gateway não precisa do segredo | ❌ Segredo compartilhado entre Keycloak e Gateway |
| Rotação de chaves | ✅ Sem impacto nos serviços (JWKS automático) | ❌ Requer atualização em todos os serviços |
| Uso em multi-serviço | ✅ Ideal | ❌ Problemático |

**RS256 é obrigatório** em arquiteturas com múltiplos serviços que precisam validar tokens sem compartilhar segredos.

---

## 3. Configuração do Keycloak (Realm cashflow)

### 3.1 Realm Settings

```json
{
  "realm": "cashflow",
  "enabled": true,
  "accessTokenLifespan": 3600,
  "ssoSessionMaxLifespan": 36000,
  "refreshTokenMaxReuse": 0,
  "revokeRefreshToken": true,
  "sslRequired": "external",
  "bruteForceProtected": true,
  "failureFactor": 5,
  "waitIncrementSeconds": 60,
  "maxFailureWaitSeconds": 900
}
```

### 3.2 Client: cashflow-api

```json
{
  "clientId": "cashflow-api",
  "enabled": true,
  "protocol": "openid-connect",
  "publicClient": false,
  "standardFlowEnabled": true,
  "directAccessGrantsEnabled": true,
  "serviceAccountsEnabled": false,
  "authorizationServicesEnabled": false,
  "attributes": {
    "access.token.lifespan": "3600",
    "use.refresh.tokens": "true",
    "client.session.max.lifespan": "36000"
  }
}
```

### 3.3 Brute Force Protection

```
Configuração recomendada:
- Máximo de falhas de login: 5
- Espera após 5 falhas: 60 segundos
- Espera máxima após falhas repetidas: 15 minutos
- Lock permanente: não (apenas bloqueio temporário)
```

---

## 4. RBAC — Role-Based Access Control

### 4.1 Modelo de Roles

```
┌─────────────────────────────────────────────────────────┐
│                    ROLES DO SISTEMA                     │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  transactions:read   → Listar e consultar transações    │
│  transactions:write  → Criar novas transações           │
│  consolidation:read  → Consultar saldo consolidado      │
│  admin               → Acesso completo + observabilidade│
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### 4.2 Mapeamento de Roles por Endpoint

| Endpoint | Método | Role Necessária | Status sem Role |
|----------|--------|-----------------|-----------------|
| `/api/v1/transactions` | POST | `transactions:write` | 403 Forbidden |
| `/api/v1/transactions` | GET | `transactions:read` | 403 Forbidden |
| `/api/v1/transactions/{id}` | GET | `transactions:read` | 403 Forbidden |
| `/api/v1/consolidation/daily` | GET | `consolidation:read` | 403 Forbidden |
| `/api/v1/consolidation/daily/{date}` | GET | `consolidation:read` | 403 Forbidden |
| `/health` | GET | Nenhuma (público) | — |

### 4.3 Perfis de Usuário e Roles Associadas

| Perfil | Roles | Justificativa |
|--------|-------|---------------|
| **Comerciante** | `transactions:read`, `transactions:write`, `consolidation:read` | Acesso completo às funcionalidades do sistema |
| **Gerente Financeiro** | `transactions:read`, `consolidation:read` | Acesso de leitura (não pode criar lançamentos) |
| **Admin / DevOps** | Todos + acesso a dashboards de observabilidade | Operação e manutenção do sistema |
| **Sistema (serviço a serviço)** | Não se aplica | Comunicação via RabbitMQ, sem JWT |

### 4.4 Verificação de Authorization no Gateway

```
Fluxo de decisão de autorização:

1. Token JWT válido? (ver seção 2.3)
   → Não: 401 Unauthorized (não autenticado)
   → Sim: próximo

2. Endpoint requer role específica?
   → POST /transactions → role: transactions:write
   → GET  /transactions → role: transactions:read
   → GET  /consolidation → role: consolidation:read

3. Token contém a role necessária?
   → Verificar realm_access.roles[]
   → Não: 403 Forbidden (autenticado mas sem permissão)
   → Sim: encaminhar request ao serviço downstream

4. Injetar X-User-Id (claim sub) no request
5. Encaminhar para serviço downstream
```

### 4.5 Por que RBAC e não ABAC?

**RBAC (Role-Based Access Control)** foi escolhido porque:
- ✅ Simples de implementar e auditar
- ✅ Suportado nativamente pelo Keycloak
- ✅ Suficiente para o contexto single-tenant do MVP
- ✅ Escalável para novos perfis sem reescrita

**ABAC (Attribute-Based Access Control)** seria considerado se:
- Multi-tenancy com isolamento por `merchantId`
- Acesso condicional por horário, localização ou contexto
- Regras de acesso muito granulares por instância de recurso

*Documentado como evolução futura caso multi-tenancy seja implementado.*

---

## 5. Ciclo de Vida dos Tokens

### 5.1 Configurações de Tempo

| Token | Validade | Renovação |
|-------|----------|-----------|
| Access Token (JWT) | 1 hora | Não renovável — usar refresh token |
| Refresh Token | 7 dias | Renovado a cada uso (rotation) |
| Session (SSO) | 10 horas | Renovada se houver atividade |

**Justificativa:**
- **1 hora para access token:** Janela curta limita o dano em caso de vazamento. Forçar refresh periódico garante que roles revogadas no Keycloak se propagam em até 1 hora.
- **7 dias para refresh token:** Período longo evita que o comerciante precise fazer login diariamente; rotation impede replay de refresh tokens roubados.

### 5.2 Revogação de Tokens

**Cenários que requerem revogação:**
- Usuário relata comprometimento de credenciais
- Usuário desabilitado pelo admin
- Mudança de roles (ex: remoção de permissão)

**Como revogar:**
```
1. Admin via Keycloak UI: Sessões do usuário → Logout all sessions
2. Via Keycloak Admin API:
   POST /admin/realms/cashflow/users/{userId}/logout
3. Tokens emitidos após revogação são inválidos
4. Access tokens já emitidos permanecem válidos até expiração (1h)
   → Para revogação imediata: usar introspection endpoint
```

**Limitação conhecida (JWT stateless):** Access tokens são stateless por natureza — uma vez emitidos, o Gateway valida a assinatura sem consultar o Keycloak. Para revogação imediata (antes do `exp`), seria necessário habilitar a validação via introspection endpoint, com custo de latência adicional. Documentado como trade-off aceito no MVP.

### 5.3 Rotação de Chaves de Assinatura

O Keycloak suporta rotação de chaves sem downtime via **JWKS (JSON Web Key Set)**:

```
1. Admin gera novo par de chaves no Keycloak
2. Nova chave pública adicionada ao JWKS endpoint
   (GET /realms/cashflow/protocol/openid-connect/certs)
3. Novos tokens são assinados com a nova chave (novo kid)
4. Tokens antigos continuam válidos (validados com chave antiga no JWKS)
5. Após expiração de todos os tokens com kid antigo → chave antiga pode ser removida

Impacto: Zero downtime — Gateway re-busca JWKS automaticamente
```

---

## 6. Comunicação Serviço-a-Serviço

### 6.1 Transactions → Consolidation Worker (via RabbitMQ)

A comunicação entre os serviços é exclusivamente **assíncrona via RabbitMQ**. Não há chamadas HTTP diretas entre os serviços internos.

**Controles de segurança na mensageria:**
- Autenticação AMQP com usuário e senha (C19)
- Rede isolada `backend-net` (C20)
- Mensagens não carregam credentials — apenas dados de negócio e `userId` para auditoria
- TLS na comunicação AMQP em produção (opcional no dev)

### 6.2 Por que não JWT entre serviços?

| Abordagem | Avaliação |
|-----------|-----------|
| **JWT de serviço (service account)** | Overhead para serviços que comunicam via fila — exigiria assinar e validar JWT em cada mensagem |
| **mTLS entre serviços** | Estratégia ideal para produção com Kubernetes e service mesh; no MVP Docker, o isolamento de rede oferece proteção equivalente |
| **Isolamento de rede Docker** | ✅ Escolhido para MVP — serviços na `backend-net` não são acessíveis externamente |

**Estratégia de produção:** Em ambiente Kubernetes, o isolamento de rede é substituído por **mTLS via Istio/Linkerd** (service mesh), garantindo autenticação mútua entre todos os serviços. Ver `docs/security/04-data-protection.md` para detalhes.

---

## 7. Exemplos de Requisições

### 7.1 Obter Token de Acesso

```http
POST http://localhost:8443/realms/cashflow/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&client_id=cashflow-api
&client_secret=<client-secret>
&username=comerciante@exemplo.com
&password=<senha>
```

**Resposta:**
```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6...",
  "expires_in": 3600,
  "refresh_expires_in": 604800,
  "refresh_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6...",
  "token_type": "Bearer",
  "session_state": "uuid",
  "scope": "openid profile email"
}
```

### 7.2 Criar Lançamento com Token

```http
POST http://localhost:8080/api/v1/transactions
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6...
Content-Type: application/json

{
  "type": "CREDIT",
  "amount": 500.00,
  "description": "Venda do dia",
  "category": "Sales",
  "date": "2024-03-15"
}
```

> ⚠️ `userId` **não é enviado** no body — é extraído do JWT pelo Gateway e injetado como `X-User-Id`.

### 7.3 Exemplos de Respostas de Erro de Autenticação

**Token ausente:**
```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="cashflow"

{
  "error": "unauthorized",
  "message": "Authorization header is missing or malformed"
}
```

**Token expirado:**
```http
HTTP/1.1 401 Unauthorized
WWW-Authenticate: Bearer realm="cashflow", error="invalid_token", error_description="Token has expired"

{
  "error": "invalid_token",
  "message": "Access token has expired"
}
```

**Role insuficiente:**
```http
HTTP/1.1 403 Forbidden

{
  "error": "forbidden",
  "message": "Insufficient permissions. Required role: transactions:write"
}
```

---

## 8. Decisões de Segurança — Justificativas

| Decisão | Justificativa |
|---------|---------------|
| **Keycloak como IdP** | Open-source, OAuth2/OIDC completo, RBAC nativo, amplamente adotado; evita implementação custom de autenticação (risco alto) |
| **RS256 (não HS256)** | Chave pública pode ser distribuída sem compartilhar o segredo; suporta rotação de chaves via JWKS |
| **JWT stateless** | Sem consulta ao banco a cada requisição — baixa latência; trade-off: revogação não é imediata |
| **Refresh token rotation** | Mitiga roubo de refresh tokens; cada uso invalida o token anterior |
| **Brute force protection** | Proteção contra ataques de força bruta no endpoint de autenticação do Keycloak |
| **RBAC no Gateway** | Verificação centralizada — serviços downstream não precisam reimplementar lógica de autorização |
| **userId pelo Gateway, não pelo cliente** | Previne forja de identidade; non-repudiation garantido (ver ADR-003) |

---

## Referências

- [RFC 6749 — OAuth 2.0](https://tools.ietf.org/html/rfc6749)
- [RFC 7519 — JWT](https://tools.ietf.org/html/rfc7519)
- [RFC 7636 — PKCE](https://tools.ietf.org/html/rfc7636)
- [Keycloak Documentation](https://www.keycloak.org/docs/latest/server_admin/)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- `docs/decisions/ADR-003-user-context-propagation.md` — Propagação de userId
- `docs/decisions/ADR-005-authentication-strategy.md` — Justificativa da escolha do Keycloak
- `docs/requirements/02-non-functional-requirements.md` — Seções 3.1, 3.2

---

**Próximo documento:** `docs/security/03-api-protection.md`
