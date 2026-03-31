# ADR-009: RBAC Scope para MVP — Dois Papéis Básicos (Admin, User)

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-009 |
| **Status** | Accepted |
| **Data** | 2026-03-31 |
| **Decisores** | Time de Arquitetura |
| **ADRs Relacionadas** | [ADR-005](ADR-005-authentication-strategy.md) (Authentication), [ADR-007](ADR-007-defense-in-depth-jwt.md) (JWT Validation) |

---

## Contexto e Problema

O CashFlow exige controle de acesso baseado em papéis (RBAC) para proteger operações sensíveis. No entanto, o MVP tem escopo definido (Transações + Consolidação) e orçamento limitado.

### Problemas Identificados

1. **Granularidade:** Quantos papéis definem MVP?
   - Overkill: 10+ roles (admin, manager, controller, operator, auditor, ...)
   - Pragmático: 2 roles (Admin, User)

2. **Autorização:** Como associar JWT claims a papéis?
   - Keycloak groups → Simplício
   - Custom attributes → Flexível mas overhead

3. **Implementação:** Como validar papéis nos endpoints?
   - Middleware global → Todos endpoints
   - Attribute-based → Seletivo
   - Ambos?

### Forças em Tensão

| Força | Pressão |
|-------|---------|
| **Segurança** | Cada operação crítica deve exigir autorização vs |
| **MVP Simplicidade** | Dois papéis cobrem 80% dos casos de uso |
| **Granularidade** | Fine-grained permissions para auditoria vs |
| **Pragmatismo** | MVP não precisa de 10 roles diferentes |
| **Escalabilidade** | RBAC deve suportar evolução para Phase 2 vs |
| **Keycloak Config** | Simples (groups) vs Complexa (custom attributes) |

### Problema Central

**Qual conjunto de RBAC roles é suficiente para MVP mantendo path claro para Phase 2?**

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| MVP tem 2 bounded contexts (Transactions, Consolidation) | Scope do projeto |
| Todas operações requerem autenticação (OAuth2) | ADR-005 |
| Apenas 2 níveis de acesso identificados no MVP | Requisitos MVP |
| Keycloak groups são idiomáticas para OAuth2 | Padrão de mercado |
| Phase 2 pode adicionar roles conforme necessário | Evolução planejada |
| Auditoria exige rastreabilidade de ações por papel | RNF de auditoria |

---

## Opções Consideradas

1. **Dois papéis básicos (Admin, User) com Keycloak groups** ← **escolhida**
2. Granular RBAC (10+ roles: Admin, Manager, Operator, Auditor, ...)
3. ABAC (Attribute-Based Access Control) com custom claims
4. Sem diferenciação de papéis (autenticação only)
5. Role-free com permissões por ownership (tenant/organization)

---

## Análise Comparativa

### Opção 2: Granular RBAC (10+ Roles)

| Critério | Avaliação |
|----------|-----------|
| Flexibilidade | ✅ Altamente granular |
| MVP Necessidade | ❌ Overkill para escopo atual |
| Keycloak Config | ❌ Múltiplos groups, combinações |
| Manutenção | ❌ Overhead de gestão de roles |
| **Veredicto** | **Descartado — complexidade prematura para MVP** |

### Opção 3: ABAC com Custom Claims

| Critério | Avaliação |
|----------|-----------|
| Flexibilidade | ✅ Arbitrariamente granular |
| Implementação | ❌ Mapper customizado no Keycloak |
| Performance | ⚠️ Parsing de claims adicional |
| Manutenção | ❌ Complexo de debugar |
| **Veredicto** | **Descartado — overhead sem benefício MVP** |

### Opção 4: Sem Papéis (Autenticação Only)

| Critério | Avaliação |
|----------|-----------|
| Segurança | ❌ Operações críticas sem autorização |
| Admin Protection | ❌ Qualquer usuário pode criar transações |
| **Veredicto** | **Descartado — viola requisitos de segurança** |

### Opção 5: Role-Free (Ownership-Based)

| Critério | Avaliação |
|----------|-----------|
| Simplicidade | ✅ Sem roles explícitos |
| Auditoria | ❌ Impossível saber quem é "admin" |
| Admin Actions | ❌ Sem forma de representar operações elevadas |
| **Veredicto** | **Descartado — não diferencia privilégios administrativos** |

### Opção 1: Dois Papéis Básicos (escolhida)

| Critério | Avaliação |
|----------|-----------|
| MVP Necessidade | ✅ Cobre 80% de casos de uso |
| Simplicidade | ✅ Dois groups no Keycloak |
| Implementação | ✅ Middleware único `[Authorize(Roles = "Admin")]` |
| Keycloak Config | ✅ Groups nativos (padrão OAuth2) |
| Auditoria | ✅ Rastreável por papel |
| Phase 2 Path | ✅ Adicionar roles sem quebrar MVP |
| **Veredicto** | ✅ **Escolhida** |

---

## Decisão

**Decidimos implementar RBAC com dois papéis básicos para o MVP: "Admin" e "User". Papéis são gerenciados como Keycloak Groups, propagados via JWT claim "roles". Operações críticas exigem "Admin"; leituras exigem "User" ou "Admin".**

### Papéis do MVP

| Papel | Responsabilidades | Endpoints |
|-------|-------------------|-----------|
| **Admin** | Configuração, operações elevadas | POST /transactions (criar), DELETE /transactions/{id} |
| **User** | Leitura de dados, consultas | GET /transactions, GET /consolidations/{date} |

### Mapeamento Keycloak → JWT

**Keycloak Groups:**
```
/admin       → JWT claim "roles": ["admin"]
/users       → JWT claim "roles": ["user"]
```

Cada usuário pertence a **exatamente um** group (admin ou user).

### Fluxo de Autorização

```
Client → JWT com claim "roles": ["admin"] ou ["user"]
         ↓
API Gateway → Valida JWT, propaga roles
         ↓
Endpoint com [Authorize(Roles = "Admin")]
         ↓
Autorização bem-sucedida → Processa requisição
ou
401 Forbidden → Rejeita com erro
```

---

## Consequências

### Positivas ✅

- **MVP Pragmatismo:** Dois roles cobrem escopo de MVP
- **Keycloak Native:** Groups são conceito nativo em OAuth2
- **Implementação Simples:** Middleware/attributes padrão do .NET
- **Auditoria Rastreável:** Cada ação registra role do executor
- **Phase 2 Ready:** Adicionar novos roles não quebra MVP

### Negativas — Trade-offs Aceitos ⚠️

- **Granularidade Limitada:** Operações dentro de "User" não diferenciadas por permissão
- **Escalabilidade Futura:** Phase 2 pode requerer redesign (não recomendado adicionar > 5 roles)
- **Ownership-Based Access:** Usuário user-a não pode ver dados de user-b (mesmo papel não diferencia)

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Requisito new de role não previsto em MVP | Média | Médio | Phase 2: estender RBAC com análise de requisitos |
| Confusion: "admin" pode significar diferentes coisas | Baixa | Médio | Documentar scope de cada papel explicitamente |
| Grupo Keycloak mal configurado → usuários sem access | Baixa | Alto | Validação em startup: verificar groups existem |

---

## Phase 2 Evolution

**Quando:** Requisitos novos de segurança identificados

**Exemplo Phase 2 Scenario:**
```
Novos papéis:
- "auditor"     → Acesso read-only a logs, sem modificação
- "reconciler"  → Acesso a endpoints de reconciliação
- "operator"    → Acesso a operações em batch
```

**Migração:** Adicionar roles sem quebrar MVP porque:
- MVP usa `[Authorize(Roles = "Admin")]` — novo role "reconciler" não herda
- Endpoints novos usam `[Authorize(Roles = "Reconciler")]` — segregado
- Code de MVP permanece inalterado

---

## Referências

- `docs/security/01-security-architecture.md` — Seção "RBAC Implementation"
- [Keycloak User Groups](https://www.keycloak.org/docs/latest/server_admin/index.html#_groups)
- [OAuth 2.0 scope design](https://datatracker.ietf.org/doc/html/rfc6749#section-3.3) — contexto de "roles" vs "scopes"
- ADR-005: `docs/decisions/ADR-005-authentication-strategy.md` — Keycloak como IdP
- ADR-007: `docs/decisions/ADR-007-defense-in-depth-jwt.md` — JWT validation

---

## Histórico de Revisões

### Revisão 1 (2026-03-31) — Decisão Inicial

Status: Accepted — Dois papéis (Admin, User) via Keycloak Groups para MVP, com path claro para Phase 2.
