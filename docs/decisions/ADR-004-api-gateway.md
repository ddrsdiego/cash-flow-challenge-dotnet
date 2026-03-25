# ADR-004: API Gateway com YARP

## Metadata

| Campo | Valor |
|-------|-------|
| **ID** | ADR-004 |
| **Status** | Accepted |
| **Data** | 2026-03-19 |
| **Última Revisão** | 2026-03-25 (ajuste menor — clarificação defense in depth JWT) |
| **Decisores** | Time de Arquitetura |
| **Revisores** | — |
| **ADRs Relacionadas** | [ADR-003](ADR-003-user-context-propagation.md), [ADR-005](ADR-005-authentication-strategy.md) |

---

## Contexto e Problema

O sistema expõe dois serviços de aplicação distintos — Transactions Service e Consolidation Service — que precisam ser acessados por clientes externos.

Sem uma camada de entrada centralizada, cada serviço seria obrigado a implementar individualmente as responsabilidades transversais da plataforma: autenticação, autorização, rate limiting, headers de segurança, logging de auditoria e propagação de identidade. Isso cria duplicação de políticas, risco de inconsistências de segurança e exposição direta dos serviços à internet.

### Problema de Segurança

A exposição direta de múltiplos serviços na internet amplifica a superfície de ataque. Cada serviço se torna um vetor independente de ataque. A ausência de um ponto de controle centralizado significa que uma falha de implementação em um único serviço pode comprometer toda a plataforma.

Além disso, o desafio exige explicitamente:

> "Proteção de APIs", "Autenticação", "Autorização" e "Controle de acesso entre serviços"

Esses requisitos são naturalmente satisfeitos por um ponto de entrada centralizado que aplica as políticas de forma consistente antes que qualquer requisição alcance os serviços de negócio.

### Problema Operacional

Sem centralização, adicionar um novo serviço à plataforma exigiria reimplementar todas as políticas de segurança e observabilidade. A manutenção de configurações espalhadas por múltiplos serviços aumenta o risco operacional.

---

## Drivers de Decisão

| Driver | Fonte |
|--------|-------|
| Centralizar autenticação e autorização — serviços downstream não reimplementam políticas | RNF — Segurança |
| Rate limiting global como proteção contra abuso e ataques de volumetria | RNF — Disponibilidade |
| Propagar identidade do usuário de forma segura (ADR-003) | ADR-003 — User Context Propagation |
| Isolar serviços internos da internet — sem exposição direta de portas | RNF — Proteção de APIs |
| Manter homogeneidade de stack .NET — sem adicionar outra tecnologia de plataforma | Princípio de simplicidade operacional |
| Correlação de rastreabilidade distribuída desde o ponto de entrada | RNF — Observabilidade |

---

## Opções Consideradas

1. **YARP (Yet Another Reverse Proxy) — .NET 8** ← **escolhida**
2. Nginx como reverse proxy
3. Envoy Proxy
4. Kong Gateway / KrakenD
5. Ocelot (.NET)
6. Sem API Gateway (exposição direta dos serviços)

---

## Análise Comparativa

### Opção 6: Sem API Gateway

Exposição direta dos serviços Transactions e Consolidation na internet.

| Critério | Avaliação |
|----------|-----------|
| Superfície de ataque | ❌ Cada serviço é um vetor independente de ataque |
| Consistência de políticas | ❌ Cada serviço implementa (ou omite) autenticação e rate limiting |
| Evolução | ❌ Adicionar novos serviços multiplica a complexidade de segurança |
| **Veredicto** | **Descartado — viola requisitos fundamentais de segurança** |

---

### Opção 2: Nginx

Nginx é um dos reverse proxies mais utilizados, com performance muito alta.

| Critério | Avaliação |
|----------|-----------|
| Performance | ✅ Muito alta |
| Integração com .NET | ❌ Tecnologia distinta — customizações exigem Lua ou módulos C |
| Validação de JWT com Keycloak | ⚠️ Possível via módulos externos, mas com complexidade adicional |
| Manutenção pela equipe .NET | ❌ Curva de aprendizado e stack adicional |
| Configuração dinâmica | ❌ Requer reload do processo para mudanças de rota |
| **Veredicto** | **Descartado — stack heterogêneo sem ganho funcional para o contexto** |

---

### Opção 3: Envoy Proxy

Envoy é o proxy de alto desempenho do ecossistema de service mesh, usado como sidecar no Istio.

| Critério | Avaliação |
|----------|-----------|
| Performance | ✅ Muito alta |
| Validação de JWT | ✅ Suporte nativo |
| Complexidade operacional | ❌ Alta — projetado para ambientes Kubernetes com muitos serviços |
| Fit para Docker Compose MVP | ❌ Over-engineering — configuração complexa para dois serviços |
| Manutenção pela equipe .NET | ❌ Stack completamente distinto |
| **Veredicto** | **Descartado — over-engineering para o contexto** |

---

### Opção 4: Kong Gateway / KrakenD

Kong é um API Gateway enterprise construído sobre Nginx, rico em funcionalidades via plugins.

| Critério | Avaliação |
|----------|-----------|
| Funcionalidades | ✅ Rico em plugins (auth, rate limit, logging) |
| Dependências adicionais | ❌ Requer banco de dados próprio (PostgreSQL/Cassandra) em modo completo |
| Complexidade operacional | ❌ Alta — containers adicionais, administração via API ou declarativa |
| Stack .NET | ❌ Tecnologia distinta, sem integração nativa |
| **Veredicto** | **Descartado — overhead injustificado para dois serviços** |

---

### Opção 5: Ocelot (.NET)

Ocelot é um API Gateway open-source desenvolvido especificamente para .NET.

| Critério | Avaliação |
|----------|-----------|
| Stack .NET | ✅ Nativo |
| Performance | ⚠️ Inferior ao YARP para alto throughput |
| Manutenção e roadmap | ❌ Projeto com ritmo de manutenção reduzido |
| Suporte Microsoft | ❌ Não é produto Microsoft |
| Extensibilidade | ⚠️ Mais limitado que YARP |
| **Veredicto** | **Descartado — YARP é superior em todos os critérios relevantes** |

---

### Comparativo Final

| Critério | YARP | Nginx | Envoy | Kong | Ocelot |
|----------|------|-------|-------|------|--------|
| Stack nativo .NET | ✅ | ❌ | ❌ | ❌ | ✅ |
| Extensibilidade via C# | ✅ | ❌ | ❌ | ❌ | ✅ |
| Performance para o volume do sistema | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| Complexidade operacional no MVP | ✅ Baixa | ✅ Baixa | ❌ Alta | ❌ Alta | ✅ Baixa |
| Suporte e roadmap Microsoft | ✅ | ❌ | ❌ | ❌ | ❌ |
| Integração com ASP.NET Core | ✅ Total | ❌ | ❌ | ❌ | ✅ |
| **Fit para o contexto** | ✅ **Ideal** | ⚠️ | ❌ | ❌ | ⚠️ |

---

## Decisão

**Adotar YARP como API Gateway, implementado como um projeto .NET 8 dedicado, centralizando autenticação, autorização, rate limiting, roteamento, propagação de identidade e observabilidade.**

### Responsabilidades do Gateway

O API Gateway é o único ponto de entrada público do sistema. Nenhum serviço interno é acessível externamente.

```
Internet
    │  (HTTPS)
    ▼
┌──────────────────────────────────────────────────────────────────┐
│  API GATEWAY (único ponto de entrada)                            │
│                                                                  │
│  • Rate limiting — controle de volume de requisições             │
│  • Validação de JWT — autenticação centralizada                  │
│  • Verificação de RBAC — autorização por endpoint                │
│  • Propagação de identidade — injeta header com userId           │
│  • Security headers — HSTS, X-Frame-Options, CSP, etc.           │
│  • Audit logging + tracing — toda requisição rastreada           │
│  • Roteamento — encaminha para o serviço correto                 │
└──────────────────────────────────────────────────────────────────┘
                │ (rede interna isolada)
      ┌─────────┴──────────┐
      ▼                    ▼
Transactions API    Consolidation API
(sem exposição direta à internet)
```

### Por que YARP é a escolha correta para este contexto

YARP é desenvolvido pela Microsoft, tem integração de primeira classe com o ASP.NET Core 8 e permite que toda a lógica do Gateway — incluindo middlewares de autenticação, rate limiting e propagação de identidade — seja implementada em C# com o mesmo pipeline, tooling e padrões de observabilidade dos demais serviços da plataforma. Para um sistema 100% .NET, adicionar outra tecnologia como ponto de entrada criaria heterogeneidade operacional sem benefício funcional.

---

## Consequências

### Positivas ✅

- **Segurança centralizada:** Autenticação, autorização, rate limiting e headers de segurança implementados uma única vez — nenhum serviço downstream reimplementa essas responsabilidades.
- **Superfície de ataque reduzida:** Apenas o Gateway é exposto à internet. Um atacante que comprometa um serviço downstream ainda encontra o Gateway como barreira.
- **Homogeneidade de stack:** YARP é .NET 8 — mesma linguagem, tooling, pipeline de CI/CD e padrões de observabilidade dos demais serviços.
- **Evolução sem explosão de complexidade:** Novos serviços adicionados à plataforma herdam automaticamente todas as políticas do Gateway.
- **Observabilidade unificada:** Todos os traces começam no Gateway com o mesmo identificador de correlação, propagado por toda a cadeia de chamadas.

### Negativas — Trade-offs Aceitos ⚠️

- **Single Point of Failure no MVP:** O Gateway em instância única é um SPOF — se cair, o acesso a todos os serviços é interrompido. Em produção, múltiplas réplicas com balanceamento eliminam este risco.
- **Hop adicional:** Toda requisição passa pelo Gateway antes de alcançar o serviço. O overhead é negligenciável em comparação com o benefício de centralização, mas existe.
- **Ponto de impacto centralizado:** Um bug de configuração no Gateway pode afetar todos os serviços simultaneamente. Mitigado com testes de integração cobrindo todos os cenários de autorização.

### Riscos 🔴

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Gateway indisponível (instância única no MVP) | Baixa | Alto | Health check + restart automático; múltiplas réplicas em produção |
| Misconfiguration de RBAC (rota com política incorreta) | Baixa | Alto | Testes de integração cobrindo todos os cenários de autorização por rota |
| Serviço downstream acessado diretamente (bypass do Gateway) | Baixa | Alto | Isolamento de rede — serviços internos sem portas expostas; NetworkPolicy em produção |
| Falha no cache de chaves públicas do Keycloak | Muito Baixa | Médio | Re-fetch automático quando chave não encontrada no cache |

### Evolução para Produção

| Aspecto | MVP (Docker Compose) | Produção (Kubernetes) |
|---------|---------------------|-----------------------|
| Alta disponibilidade | ❌ Instância única | ✅ Múltiplas réplicas com HPA |
| TLS termination | ⚠️ HTTP interno (dev) | ✅ TLS 1.3 com cert-manager |
| Rate limiting distribuído | ❌ In-memory por instância | ✅ Backed em Redis (compartilhado entre réplicas) |
| Isolamento de rede | ✅ Docker network | ✅ NetworkPolicy + service mesh |

---

## Referências

- [YARP Documentation — Microsoft](https://microsoft.github.io/reverse-proxy/)
- [API Gateway Pattern — microservices.io](https://microservices.io/patterns/apigateway.html)
- ADR-003 (User Context): `docs/decisions/ADR-003-user-context-propagation.md` — propagação de identidade via Gateway
- ADR-005 (Authentication): `docs/decisions/ADR-005-authentication-strategy.md` — JWT validado no Gateway
- Padrões arquiteturais: `docs/architecture/06-architectural-patterns.md` — Seção 9 (API Gateway)
- Arquitetura de segurança: `docs/security/01-security-architecture.md` — Controles C2 a C7
- Requisito não funcional: `docs/requirements/02-non-functional-requirements.md`
