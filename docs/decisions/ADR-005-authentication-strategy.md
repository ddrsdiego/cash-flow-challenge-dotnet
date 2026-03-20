# ADR-005: Estratégia de Autenticação com Keycloak

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-005 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-003](ADR-003-user-context-propagation.md), [ADR-004](ADR-004-api-gateway.md) |

---

## Contexto e Problema

O sistema precisa de uma estratégia de **autenticação e autorização** que responda a três necessidades simultâneas:

1. **Identificar quem está fazendo a requisição** — de forma confiável e não forjável
2. **Controlar o que cada perfil pode fazer** — com granularidade suficiente para diferentes papéis de negócio
3. **Garantir segurança sem implementar autenticação do zero** — autenticação customizada é um dos vetores de vulnerabilidade mais comuns em sistemas

O desafio técnico exige explicitamente:

> "A solução deve obrigatoriamente apresentar um desenho de segurança, contemplando no mínimo: Autenticação, Autorização, Proteção de dados sensíveis, Controle de acesso entre serviços."

### Por que não implementar autenticação customizada

Implementar autenticação do zero — mesmo aparentemente simples, como emitir e validar JWTs manualmente — introduz riscos significativos:

- Gestão de chaves de assinatura (geração, rotação, revogação)
- Proteção contra ataques de força bruta
- Gerenciamento de sessões e invalidação de tokens
- Conformidade com padrões reconhecidos (OAuth 2.0, OpenID Connect)
- Armazenamento seguro de credenciais

Cada um desses aspectos é um vetor de vulnerabilidade se implementado incorretamente. A adoção de um Identity Provider especializado transfere essa responsabilidade para um componente testado, auditado e amplamente adotado pela indústria.

### Requisito de Controle de Acesso Granular

O sistema possui perfis com permissões distintas:
- **Comerciante** — lança e consulta transações, consulta saldo consolidado
- **Gerente financeiro** — consulta transações e saldo, sem poder criar lançamentos
- **Administrador** — acesso completo, incluindo observabilidade

Isso exige um modelo de autorização baseado em papéis (RBAC), aplicado de forma centralizada no ponto de entrada, sem que cada serviço reimplemente a lógica de verificação de permissões.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Autenticação segura sem implementação customizada | Princípio de segurança — evitar vetores de vulnerabilidade conhecidos |
| OAuth 2.0 + OpenID Connect como padrões reconhecidos da indústria | RNF — Conformidade com padrões |
| RBAC para controle de acesso granular por perfil de negócio | RNF — Autorização |
| Self-hosted — sem dependência de serviço de terceiros ou cloud provider | Princípio de independência de vendor |
| JWT como token portador stateless — sem consulta ao banco por requisição | RNF — Performance e escalabilidade |
| Execução local sem dependência de ambiente de nuvem | Requisito de desenvolvimento e demonstração |
| Proteção contra força bruta e gestão de ciclo de vida de tokens | RNF — Segurança |

---

## Opções Consideradas

1. **Keycloak** ← **escolhido**
2. Auth0 (SaaS)
3. Azure Active Directory B2C (SaaS)
4. Duende IdentityServer / OpenIddict (.NET)
5. Autenticação customizada com JWT (ASP.NET Core Identity)

---

## Análise Comparativa

### Opção 2: Auth0

Auth0 é uma plataforma SaaS de identidade amplamente utilizada, com excelente experiência de desenvolvimento.

| Critério | Avaliação |
|----------|-----------|
| Facilidade de uso | ✅ Excelente experiência de desenvolvimento |
| OAuth2 + OIDC | ✅ Suporte completo |
| Self-hosted | ❌ SaaS apenas — depende de conectividade com a plataforma Auth0 |
| Execução local | ❌ Impossível sem conectividade externa |
| Vendor lock-in | ❌ Alto — dependência da plataforma e precificação Auth0 |
| Custo em produção | ❌ Pago acima de um volume de usuários ativos |
| **Veredicto** | **Descartado — dependência de SaaS externo inviabiliza execução local e cria lock-in** |

---

### Opção 3: Azure Active Directory B2C

Azure AD B2C é o serviço de identidade para aplicações voltadas a clientes da Microsoft.

| Critério | Avaliação |
|----------|-----------|
| OAuth2 + OIDC | ✅ Suporte completo |
| Integração com ecossistema Azure | ✅ Excelente |
| Self-hosted | ❌ Azure-only |
| Execução local | ❌ Impossível sem emuladores limitados |
| Vendor lock-in | ❌ Máximo — preso ao Azure |
| Portabilidade | ❌ Nula fora do Azure |
| **Veredicto** | **Descartado — vendor lock-in máximo e impossibilidade de execução local** |

---

### Opção 4: Duende IdentityServer / OpenIddict

Duende IdentityServer e OpenIddict são frameworks .NET para construir um servidor OAuth2/OIDC customizado.

| Critério | Avaliação |
|----------|-----------|
| Stack .NET | ✅ Nativo |
| Flexibilidade | ✅ Alta — controle total sobre o comportamento |
| Esforço de implementação | ❌ Alto — exige implementar e manter gestão de usuários, tokens, rotação de chaves |
| RBAC out-of-the-box | ❌ Precisa ser construído |
| Proteção contra força bruta | ❌ Precisa ser implementada |
| Duende IdentityServer — licença | ❌ Pago para uso comercial acima de certo volume |
| Maturidade operacional | ⚠️ Depende da qualidade da implementação própria |
| **Veredicto** | **Descartado — esforço injustificado para um componente onde soluções prontas são superiores** |

---

### Opção 5: Autenticação Customizada com JWT

Implementar emissão e validação de JWT diretamente no ASP.NET Core Identity, sem um servidor de autorização dedicado.

| Critério | Avaliação |
|----------|-----------|
| Simplicidade inicial | ✅ Aparentemente simples |
| Gestão de chaves de assinatura | ❌ Responsabilidade manual — geração, rotação, revogação |
| Proteção contra força bruta | ❌ Responsabilidade manual |
| Refresh token rotation | ❌ Responsabilidade manual |
| OAuth2 + OIDC | ❌ Não implementa os padrões da indústria |
| Auditoria e conformidade | ❌ Depende inteiramente da qualidade da implementação |
| Risco de vulnerabilidade | ❌ Alto — cada detalhe implementado incorretamente é um vetor de ataque |
| **Veredicto** | **Descartado — autenticação customizada é um dos maiores vetores de vulnerabilidade** |

---

### Comparativo Final

| Critério | Keycloak | Auth0 | Azure AD B2C | IdentityServer | Custom JWT |
|----------|----------|-------|--------------|----------------|------------|
| Self-hosted | ✅ | ❌ | ❌ | ✅ | ✅ |
| OAuth2 + OIDC completo | ✅ | ✅ | ✅ | ✅ | ❌ |
| RBAC nativo | ✅ | ✅ | ✅ | ⚠️ | ❌ |
| Execução local | ✅ | ❌ | ❌ | ✅ | ✅ |
| Sem vendor lock-in | ✅ | ❌ | ❌ | ✅ | ✅ |
| Sem esforço de implementação auth | ✅ | ✅ | ✅ | ❌ | ❌ |
| Open-source | ✅ | ❌ | ❌ | ⚠️ | ✅ |
| Sem custo adicional | ✅ | ❌ | ❌ | ⚠️ | ✅ |
| **Fit para o contexto** | ✅ **Ideal** | ❌ | ❌ | ⚠️ | ❌ |

---

## Decisão

**Adotar Keycloak como Identity Provider centralizado, com OAuth 2.0 + OpenID Connect para autenticação, JWT RS256 como formato de token, e RBAC para controle de acesso granular por perfil de negócio.**

### Modelo de Autenticação

O Keycloak é o único componente responsável por emitir e assinar tokens. O API Gateway é responsável por validar esses tokens a cada requisição, sem consultar o Keycloak — aproveitando a natureza stateless do JWT.

```
┌──────────────────────────────────────────────────────────────────┐
│                   MODELO DE AUTENTICAÇÃO                         │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  AUTENTICAÇÃO (obter token):                                     │
│  Cliente → Keycloak (credenciais)                                │
│  Keycloak → Cliente (JWT assinado com RS256)                     │
│                                                                  │
│  AUTORIZAÇÃO (usar token):                                       │
│  Cliente → API Gateway (JWT no header Authorization)             │
│  API Gateway → valida assinatura com chave pública do Keycloak   │
│             → verifica expiração, emissor, audiência             │
│             → verifica roles no JWT para o endpoint solicitado   │
│             → encaminha requisição ao serviço downstream         │
│                                                                  │
│  KEYCLOAK NÃO É CONSULTADO A CADA REQUISIÇÃO                     │
│  (JWT é stateless — chave pública em cache no Gateway)           │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

### Justificativas das Decisões Secundárias

**Por que RS256 e não HS256?**

RS256 usa criptografia assimétrica: Keycloak assina com chave privada; o Gateway valida com chave pública. Isso permite que o Gateway valide tokens sem jamais ter acesso ao segredo de assinatura, e possibilita rotação de chaves sem impacto nos serviços. HS256 exigiria compartilhar um segredo entre o Keycloak e cada serviço que valida tokens — um risco de segurança relevante em arquiteturas distribuídas.

**Por que JWT stateless e não session-based?**

JWT stateless permite que o Gateway valide tokens sem consultar o Keycloak a cada requisição — essencial para performance em cenários de 100 req/s. O trade-off aceito é que a revogação de tokens não é imediata: um token válido permanece utilizável até sua expiração (1 hora). Para revogação imediata, seria necessário ativar introspection endpoint, com custo de latência por requisição.

**Por que Resource Owner Password Flow no MVP?**

O ROPF (usuário envia credenciais diretamente para a API) é adotado no MVP pela simplicidade de integração via REST client. Em produção com frontend, o Authorization Code Flow com PKCE é o padrão recomendado — mais seguro, pois as credenciais nunca passam pelo cliente da aplicação.

### Modelo de Autorização (RBAC)

| Perfil | Permissões | Acesso |
|--------|-----------|--------|
| Comerciante | `transactions:read`, `transactions:write`, `consolidation:read` | Operação completa |
| Gerente Financeiro | `transactions:read`, `consolidation:read` | Somente leitura |
| Administrador | Todas as acima + acesso a observabilidade | Gestão da plataforma |

A verificação de roles é responsabilidade exclusiva do API Gateway — serviços downstream recebem apenas a identidade do usuário (via header), sem reimplementar lógica de autorização.

---

## Consequências

### Positivas ✅

- **Segurança sem implementação customizada:** Keycloak é testado, auditado e amplamente adotado — gestão de chaves, proteção contra força bruta e ciclo de vida de tokens são responsabilidades do componente, não da equipe.
- **Padrões da indústria:** OAuth 2.0 + OIDC são padrões reconhecidos — qualquer cliente compatível com esses protocolos pode se integrar sem customizações.
- **RBAC nativo:** Gestão de papéis e permissões via interface administrativa, sem código customizado.
- **Execução totalmente local:** Nenhuma dependência externa de cloud ou SaaS — o sistema funciona integralmente em Docker Compose.
- **Portabilidade:** Sem vendor lock-in — Keycloak pode ser substituído por qualquer outro IdP compatível com OAuth2/OIDC.
- **Rotação de chaves sem downtime:** Suporte a JWKS permite rotação de chaves de assinatura transparente para os serviços.

### Negativas — Trade-offs Aceitos ⚠️

- **Keycloak como SPOF de autenticação no MVP:** Instância única — se o Keycloak ficar indisponível, nenhuma nova autenticação é possível. Sessões com token válido ainda funcionam até a expiração. Em produção, cluster Keycloak com alta disponibilidade elimina este risco.
- **Revogação não imediata (JWT stateless):** Tokens válidos permanecem utilizáveis até o `exp`. Remoção imediata de acesso exige introspection endpoint, com custo de latência.
- **Resource Owner Password Flow no MVP:** Fluxo simplificado para demonstração. Em produção com frontend, Authorization Code + PKCE é o padrão mais seguro.
- **Complexidade operacional:** Keycloak é um componente adicional para operar, monitorar e manter — banco de dados próprio (PostgreSQL), configuração de realm, gestão de clients e users.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Keycloak indisponível (instância única no MVP) | Baixa | Alto | Tokens válidos continuam funcionando; cluster Keycloak em produção |
| Chave de assinatura comprometida | Muito Baixa | Alto | Rotação de chaves via JWKS — sem downtime; revogar tokens emitidos com chave comprometida |
| Token roubado usado dentro da janela de expiração | Baixa | Médio | Janela curta (1h) limita o dano; refresh token rotation mitiga roubo de refresh tokens |
| Configuração incorreta de roles no Keycloak | Baixa | Médio | Testes de autorização cobrindo todos os perfis e endpoints; processo de auditoria de configuração |

### Evolução para Produção

| Aspecto | MVP (Docker Compose) | Produção (Kubernetes) |
|---------|---------------------|-----------------------|
| Alta disponibilidade | ❌ Instância única | ✅ Cluster Keycloak com múltiplas réplicas |
| Fluxo de autenticação | ⚠️ Resource Owner Password Flow | ✅ Authorization Code + PKCE |
| Revogação de tokens | ⚠️ Eventual (até expiração do access token) | ✅ Introspection endpoint para revogação imediata se necessário |
| Banco de dados Keycloak | ⚠️ PostgreSQL single-node | ✅ PostgreSQL com alta disponibilidade |
| Integração com diretório corporativo | ❌ Usuários locais | ✅ LDAP/Active Directory federation |

---

## Referências

- [Keycloak Documentation](https://www.keycloak.org/docs/latest/server_admin/)
- [RFC 6749 — OAuth 2.0](https://tools.ietf.org/html/rfc6749)
- [RFC 7519 — JWT](https://tools.ietf.org/html/rfc7519)
- [RFC 7636 — PKCE](https://tools.ietf.org/html/rfc7636)
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- ADR-003 (User Context): `docs/decisions/ADR-003-user-context-propagation.md` — identidade extraída do JWT pelo Gateway
- ADR-004 (API Gateway): `docs/decisions/ADR-004-api-gateway.md` — JWT validado no ponto de entrada
- Autenticação detalhada: `docs/security/02-authentication-authorization.md`
- Arquitetura de segurança: `docs/security/01-security-architecture.md` — Controles C3, C4, C21, C22, C23
