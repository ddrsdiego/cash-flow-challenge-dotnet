# 03 — API Protection

## Visão Geral

Este documento detalha as estratégias de **proteção das APIs** do CashFlow System, abrangendo:
- Rate limiting e throttling
- Validação de inputs
- CORS (Cross-Origin Resource Sharing)
- Security headers
- Defesa contra ataques comuns (OWASP Top 10)
- Política de respostas de erro

A proteção de APIs é a linha de defesa entre o mundo externo e os serviços de negócio. Ela atua complementarmente à autenticação/autorização (doc 02), garantindo que mesmo requisições autenticadas não possam abusar do sistema.

---

## 1. Rate Limiting e Throttling

### 1.1 Política de Rate Limiting

O sistema implementa rate limiting em duas camadas: **Gateway** (global) e **Serviços** (por endpoint crítico).

| Camada | Escopo | Limite | Resposta ao Exceder |
|--------|--------|--------|---------------------|
| API Gateway (global) | Por IP | 100 req/s | `429 Too Many Requests` |
| Consolidation API | Por IP + endpoint | 50 req/s | `429 Too Many Requests` |
| Keycloak `/token` | Por IP | Keycloak built-in | `429 Too Many Requests` |

**Justificativa dos limites:**
- **100 req/s no Gateway:** Limite superior que protege a infraestrutura inteira de floods; acima desse volume, qualquer serviço downstream seria sobrecarregado
- **50 req/s no Consolidation:** Requisito explícito do desafio — "50 req/s com ≤ 5% de perda". O rate limit garante que o sistema permanece estável nesse threshold sem degradação

### 1.2 Implementação com ASP.NET Core Rate Limiter

```csharp
// API Gateway — Rate Limiter Global
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(1),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "1";
        
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please retry after 1 second.",
            retryAfter = 1
        }, cancellationToken);
    };
});
```

```csharp
// Consolidation API — Rate Limiter por Endpoint
builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("consolidation-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 50;
        limiterOptions.Window = TimeSpan.FromSeconds(1);
        limiterOptions.SegmentsPerWindow = 4;
    });
});

// Aplicar na rota:
app.MapGet("/api/v1/consolidation/daily", ...)
   .RequireRateLimiting("consolidation-policy");
```

### 1.3 Headers de Resposta do Rate Limiter

Toda resposta bem-sucedida inclui headers informativos:

```http
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 87
X-RateLimit-Reset: 2024-03-15T15:30:01Z
```

Quando o limite é excedido:
```http
HTTP/1.1 429 Too Many Requests
Retry-After: 1
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 2024-03-15T15:30:01Z

{
  "error": "rate_limit_exceeded",
  "message": "Too many requests. Please retry after 1 second.",
  "retryAfter": 1
}
```

### 1.4 Sliding Window vs Fixed Window

**Sliding Window** foi escolhido sobre Fixed Window porque:
- ✅ Evita o "burst attack" no início de cada janela (problema clássico do Fixed Window)
- ✅ Distribuição mais uniforme de requisições
- ✅ Melhor aproximação da capacidade real do sistema

```
Fixed Window — Vulnerável:
   [0s-1s] 100 req ✅ | [1s-2s] 100 req ✅
   Mas: 200 req em 100ms (no boundary) → ainda aceita!

Sliding Window — Resiliente:
   A qualquer momento, no último 1 segundo: máximo 100 req
```

---

## 2. Validação de Inputs

### 2.1 Princípio

**Nunca confiar em dados do cliente.** Toda entrada deve ser validada antes de qualquer operação de negócio ou persistência. Isso protege contra:
- Injection attacks (mesmo que MongoDB use queries parametrizadas)
- Dados que quebram regras de negócio (amounts negativos, datas inválidas)
- Payloads excessivamente grandes (DoS por payload)

### 2.2 Regras de Validação — Transactions API

#### POST /api/v1/transactions

| Campo | Tipo | Validações | Mensagem de Erro |
|-------|------|-----------|-----------------|
| `type` | string | Obrigatório; deve ser `CREDIT` ou `DEBIT` | "Type must be CREDIT or DEBIT" |
| `amount` | decimal | Obrigatório; > 0; máximo 999.999.999,99 | "Amount must be positive and less than 1 billion" |
| `description` | string | Obrigatório; não vazio; 3-500 chars; sem XSS | "Description must be between 3 and 500 characters" |
| `category` | string | Obrigatório; deve ser valor da lista permitida | "Category must be one of: Sales, Services, Supplies, Utilities, Returns, Other" |
| `date` | date (YYYY-MM-DD) | Obrigatório; não pode ser data futura; não mais de 1 ano no passado | "Date cannot be in the future or more than 1 year in the past" |

**Categorias permitidas:** `Sales`, `Services`, `Supplies`, `Utilities`, `Returns`, `Other`

#### GET /api/v1/transactions

| Parâmetro | Tipo | Validações | Mensagem de Erro |
|-----------|------|-----------|-----------------|
| `startDate` | date | Opcional; formato YYYY-MM-DD | "Invalid date format" |
| `endDate` | date | Opcional; ≥ startDate | "endDate must be after startDate" |
| `type` | string | Opcional; CREDIT ou DEBIT | "Type must be CREDIT or DEBIT" |
| `page` | int | Opcional; ≥ 1; default: 1 | "Page must be positive" |
| `pageSize` | int | Opcional; 1-100; default: 20 | "PageSize must be between 1 and 100" |

**Regra do intervalo:** `endDate - startDate ≤ 90 dias`. Limita queries pesadas no MongoDB.

### 2.3 Regras de Validação — Consolidation API

#### GET /api/v1/consolidation/daily?date=YYYY-MM-DD

| Parâmetro | Tipo | Validações | Mensagem de Erro |
|-----------|------|-----------|-----------------|
| `date` | date | Obrigatório; formato YYYY-MM-DD; não pode ser data futura | "Date cannot be in the future" |

### 2.4 Implementação com FluentValidation

```csharp
public class CreateTransactionValidator : AbstractValidator<CreateTransactionRequest>
{
    private static readonly HashSet<string> AllowedCategories = new()
    {
        "Sales", "Services", "Supplies", "Utilities", "Returns", "Other"
    };

    public CreateTransactionValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => t is "CREDIT" or "DEBIT")
            .WithMessage("Type must be CREDIT or DEBIT");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThan(1_000_000_000)
            .WithMessage("Amount must be positive and less than 1 billion");

        RuleFor(x => x.Description)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(500)
            .WithMessage("Description must be between 3 and 500 characters");

        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => AllowedCategories.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", AllowedCategories)}");

        RuleFor(x => x.Date)
            .NotEmpty()
            .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date cannot be in the future")
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)))
            .WithMessage("Date cannot be more than 1 year in the past");
    }
}
```

### 2.5 Limite de Tamanho de Payload

Para prevenir DoS por payloads grandes:

```csharp
// No Transactions API
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024; // 64KB máximo
});
```

**Justificativa:** O payload máximo do `POST /transactions` é ~1KB. Um limite de 64KB garante espaço para crescimento sem permitir payloads abusivos.

### 2.6 Formato de Resposta de Validação

```http
HTTP/1.1 400 Bad Request
Content-Type: application/json

{
  "error": "validation_failed",
  "message": "One or more validation errors occurred",
  "errors": [
    {
      "field": "amount",
      "message": "Amount must be positive and less than 1 billion"
    },
    {
      "field": "category",
      "message": "Category must be one of: Sales, Services, Supplies, Utilities, Returns, Other"
    }
  ],
  "traceId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**Nota:** O `traceId` permite correlacionar o erro com logs do Seq/Jaeger sem expor detalhes internos do sistema.

---

## 3. CORS (Cross-Origin Resource Sharing)

### 3.1 Política de CORS

O API Gateway implementa a política de CORS centralizada. Serviços downstream não implementam CORS — apenas o Gateway é exposto externamente.

**Configuração para MVP:**
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("CashFlowPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",      // Dev frontend
                "https://cashflow.empresa.com" // Produção
            )
            .WithMethods("GET", "POST")       // Apenas métodos usados
            .WithHeaders(
                "Authorization",
                "Content-Type",
                "X-Request-Id"
            )
            .WithExposedHeaders(
                "X-RateLimit-Limit",
                "X-RateLimit-Remaining",
                "Retry-After"
            )
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});
```

### 3.2 Decisões de CORS

| Decisão | Justificativa |
|---------|---------------|
| **Origins explícitas (não `*`)** | Wildcard permite requisições de qualquer origem — risco de CSRF em combinação com cookies |
| **Métodos explícitos** | Apenas GET e POST são necessários — sem PUT, PATCH, DELETE expostos |
| **Headers explícitos** | Apenas o necessário para funcionamento — reduz superfície de ataque |
| **Preflight cache de 10min** | Reduz requisições OPTIONS repetidas sem sacrificar segurança |

### 3.3 Por que CORS não protege completamente

CORS é uma proteção do **browser** — não é uma medida de segurança de servidor. APIs que aceitam tokens JWT não dependem exclusivamente de CORS:
- Requisições diretas (curl, Postman, scripts) não são afetadas por CORS
- A proteção real vem da validação do JWT (doc 02)
- CORS protege especificamente contra **CSRF em browsers** quando cookies são usados

*No CashFlow System, que usa JWT no header (não cookie), CORS é complementar, não crítico.*

---

## 4. Security Headers

### 4.1 Headers Implementados no API Gateway

```http
# Todos os responses incluem:

Strict-Transport-Security: max-age=31536000; includeSubDomains; preload
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 0
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: camera=(), microphone=(), geolocation=()
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
Cache-Control: no-store
```

### 4.2 Descrição e Justificativa de Cada Header

| Header | Valor | Proteção | Justificativa |
|--------|-------|----------|---------------|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains; preload` | Força HTTPS | Previne downgrade para HTTP; `preload` permite inclusão na lista HSTS de browsers |
| `X-Content-Type-Options` | `nosniff` | Previne MIME sniffing | Impede browser de interpretar resposta como tipo diferente do declarado |
| `X-Frame-Options` | `DENY` | Previne clickjacking | APIs não devem ser carregadas em iframes — `DENY` é mais restritivo que `SAMEORIGIN` |
| `X-XSS-Protection` | `0` | (desabilita proteção legada) | A proteção XSS de browsers antigos pode criar vulnerabilidades; CSP é o controle correto |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | Controla Referrer | Não vaza path completo em requisições cross-origin |
| `Permissions-Policy` | Tudo desabilitado | Restringe features do browser | APIs não precisam de câmera, microfone ou geolocalização |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` | Previne XSS e clickjacking | APIs não servem HTML — CSP restritivo é adequado |
| `Cache-Control` | `no-store` | Previne cache de dados sensíveis | Dados financeiros não devem ser cacheados em proxies intermediários |

### 4.3 Implementação no YARP

```csharp
app.Use(async (context, next) =>
{
    var response = context.Response;

    response.Headers["Strict-Transport-Security"] =
        "max-age=31536000; includeSubDomains; preload";
    response.Headers["X-Content-Type-Options"] = "nosniff";
    response.Headers["X-Frame-Options"] = "DENY";
    response.Headers["X-XSS-Protection"] = "0";
    response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    response.Headers["Content-Security-Policy"] =
        "default-src 'none'; frame-ancestors 'none'";
    response.Headers["Cache-Control"] = "no-store";

    // Remover headers que revelam informação do servidor
    response.Headers.Remove("Server");
    response.Headers.Remove("X-Powered-By");
    response.Headers.Remove("X-AspNet-Version");

    await next();
});
```

### 4.4 Headers de Resposta Removidos

Para evitar information disclosure sobre a stack tecnológica:

| Header Removido | Por que é Problemático |
|-----------------|----------------------|
| `Server: Kestrel` | Revela o servidor web usado |
| `X-Powered-By: ASP.NET` | Revela o framework usado |
| `X-AspNet-Version` | Revela a versão específica |

---

## 5. Defesa contra Ataques Comuns (OWASP Top 10)

### 5.1 Mapeamento OWASP → Controles

| OWASP 2021 | Risco | Controles Implementados |
|------------|-------|------------------------|
| **A01 — Broken Access Control** | Acessar recursos de outros usuários | JWT + RBAC (doc 02); `userId` extraído de JWT (ADR-003) |
| **A02 — Cryptographic Failures** | Dados em texto claro | TLS 1.3 em trânsito; encryption at rest em produção (doc 04) |
| **A03 — Injection** | Injeção de código via input | FluentValidation; MongoDB usa queries parametrizadas (nativo do driver) |
| **A04 — Insecure Design** | Design sem segurança | Threat model documentado; defense in depth; fail-secure |
| **A05 — Security Misconfiguration** | Configuração padrão insegura | Security headers; sem endpoints de debug em produção; secrets via variáveis de ambiente |
| **A06 — Vulnerable Components** | Dependências com CVEs | Dependabot/OWASP Dependency Check no pipeline CI/CD |
| **A07 — Auth & Identification Failures** | Falhas de autenticação | Keycloak com brute force protection; RS256; refresh token rotation |
| **A08 — Software Integrity Failures** | Pipeline comprometido | Multi-stage Docker build; imagens verificadas por digest |
| **A09 — Logging & Monitoring Failures** | Falhas sem detecção | OpenTelemetry + Seq + Prometheus + alertas (doc observabilidade) |
| **A10 — SSRF** | Request forgery no servidor | Não aplicável — sistema não faz fetch de URLs fornecidas por usuários |

### 5.2 Proteção contra Injection (Detalhe)

**Por que MongoDB não é imune a injection?**

Embora MongoDB não use SQL (portanto não há SQL injection), **NoSQL injection** é possível se inputs não forem validados:

```javascript
// Cenário vulnerável (sem validação):
// Input: { "category": { "$ne": null } }
// Resultado: retorna TODOS os documentos (bypass de filtro)

// Proteção no CashFlow:
// 1. FluentValidation garante que category é string (não objeto)
// 2. Driver .NET serializa valores tipados (não JSON bruto)
// 3. Queries usam FilterDefinitionBuilder (não strings)
```

### 5.3 Proteção contra Credential Stuffing

```
Keycloak — Configurações Anti-Credential Stuffing:
- Brute force protection: bloqueio após 5 tentativas
- Espera progressiva: 60s → 5min → 15min
- Rate limiting no endpoint /token via API Gateway
- Logs de tentativas de autenticação no Seq
- Alertas Prometheus: error rate > 10% no endpoint /token
```

### 5.4 Proteção contra Parameter Pollution

Requisições com parâmetros duplicados são normalizadas pelo ASP.NET Core:

```http
# Request com parameter pollution:
GET /api/v1/transactions?type=CREDIT&type=DEBIT

# ASP.NET Core: type = ["CREDIT", "DEBIT"] (array)
# Validação: type deve ser string, não array → 400 Bad Request
```

### 5.5 Proteção contra Requests Grandes (DoS)

```
Limites configurados:
- Tamanho máximo do body: 64KB (Kestrel)
- Timeout de request: 30 segundos
- Conexões simultâneas máximas: configurável por ambiente

Headers verificados:
- Content-Length deve corresponder ao body real
- Content-Type deve ser application/json para endpoints que aceitam body
```

---

## 6. Política de Respostas de Erro

### 6.1 Princípio: Mínimo de Informação

Respostas de erro **não devem revelar detalhes internos** do sistema:

| ❌ Não fazer | ✅ Fazer |
|-------------|---------|
| Expor stack trace | Retornar mensagem genérica |
| Revelar nome de tabelas/collections | Usar mensagens de negócio |
| Mostrar versão de dependências | Logar internamente, responder genericamente |
| Diferenciar "usuário não existe" de "senha errada" | "Invalid credentials" para ambos |

### 6.2 Formato Padronizado de Erros

```json
{
  "error": "string (código do erro — snake_case)",
  "message": "string (mensagem amigável, sem detalhes internos)",
  "traceId": "uuid (para correlação nos logs — informa ao usuário para acionar suporte)",
  "errors": [
    {
      "field": "string (campo com erro, apenas em validações)",
      "message": "string (descrição do problema no campo)"
    }
  ]
}
```

### 6.3 Mapeamento de Status Codes

| Status | Cenário | Inclui `errors`? | Inclui `traceId`? |
|--------|---------|-----------------|------------------|
| `400` | Validação de input falhou | ✅ Sim | ✅ Sim |
| `401` | Token ausente ou inválido | ❌ Não | ✅ Sim |
| `403` | Role insuficiente | ❌ Não | ✅ Sim |
| `404` | Recurso não encontrado | ❌ Não | ✅ Sim |
| `422` | Dados semanticamente inválidos (regra de negócio) | ✅ Sim | ✅ Sim |
| `429` | Rate limit excedido | ❌ Não | ✅ Sim |
| `500` | Erro interno do servidor | ❌ Não | ✅ Sim |
| `503` | Serviço indisponível | ❌ Não | ✅ Sim |

**Para erros 5xx:** A mensagem retornada é sempre genérica ("An unexpected error occurred"). O `traceId` permite ao suporte localizar o erro real nos logs do Seq/Jaeger sem expor detalhes ao usuário final.

---

## 7. Monitoramento e Alertas de Segurança

### 7.1 Métricas de Segurança Monitoradas

| Métrica | Threshold de Alerta | Ação |
|---------|--------------------|----|
| `http_requests_total{status="401"}` | > 50/min | Possível ataque de força bruta; verificar logs |
| `http_requests_total{status="403"}` | > 20/min | Possível tentativa de escalada de privilégio |
| `http_requests_total{status="429"}` | > 100/min | Ataque de flood; considerar bloqueio por IP |
| `http_requests_total{status="400"}` | > 200/min | Possível fuzzing de parâmetros |
| DLQ size | > 0 mensagens | Mensagem falhou 3x — investigar |

### 7.2 Log de Auditoria de Segurança

Eventos de segurança logados estruturadamente no Seq:

```json
{
  "timestamp": "2024-03-15T15:30:45.123Z",
  "level": "WARNING",
  "event": "AUTH_FAILURE",
  "service": "api-gateway",
  "traceId": "550e8400-...",
  "sourceIp": "192.168.1.100",
  "endpoint": "POST /api/v1/transactions",
  "reason": "JWT_EXPIRED",
  "userId": null
}
```

```json
{
  "timestamp": "2024-03-15T15:31:00.000Z",
  "level": "WARNING",
  "event": "RATE_LIMIT_EXCEEDED",
  "service": "api-gateway",
  "traceId": "650e8400-...",
  "sourceIp": "192.168.1.100",
  "endpoint": "GET /api/v1/consolidation/daily",
  "requestsInWindow": 101
}
```

---

## Referências

- [OWASP API Security Top 10](https://owasp.org/API-Security/)
- [OWASP Cheat Sheet — Rate Limiting](https://cheatsheetseries.owasp.org/cheatsheets/Denial_of_Service_Cheat_Sheet.html)
- [OWASP Cheat Sheet — Input Validation](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html)
- [Security Headers — securityheaders.com](https://securityheaders.com/)
- [HSTS Preload](https://hstspreload.org/)
- [FluentValidation Documentation](https://docs.fluentvalidation.net/)
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- `docs/security/01-security-architecture.md` — Inventário de controles
- `docs/security/02-authentication-authorization.md` — JWT e RBAC
- `docs/requirements/02-non-functional-requirements.md` — Seções 1.1, 3.5, 3.7

---

**Próximo documento:** `docs/security/04-data-protection.md`
