# Implementation Plan — Feedback Remediation

Remediation of technical feedback received for the CashFlow System: unit tests for Consolidation, RBAC implementation per ADR-009, and documentation alignment.

## [Overview]

This plan addresses four concrete gaps identified after deep code inspection: (1) missing unit tests for the Consolidation API handlers, (2) RBAC roles not enforced despite ADR-009 decision, (3) README inconsistencies about Redis/IMemoryCache and test status, and (4) documentation in docs/ misaligned with actual implementation.

The existing codebase has 30 passing tests (18 in Transactions.Tests + 12 in Worker.Tests), correct userId filtering, and a working YARP path transform for consolidation routes. Only the gaps listed below require changes.

## [Types]

No new types are introduced. The implementation uses existing types from the codebase.

Types used in new test files:
- `GetDailyConsolidationQuery(string TracerId, string UserId, DateTime Date)` — existing record
- `InvalidateConsolidationCacheCommand(string TracerId, IReadOnlyList<string> ConsolidationKeys)` — existing record
- `UpdateConsolidationCacheCommand` — existing record (in UpdateConsolidationCache namespace)
- `IConsolidationCache` — existing interface with GetAsync/SetAsync/InvalidateAsync
- `IConsolidationQueryRepository` — existing interface with FindByKeyAsync/FindDailyConsolidationsByKeysAsync
- `ConsolidationKey` — existing value object with TryParse static method
- `DailyBalances` — existing domain entity
- `DailyConsolidationResponse` — existing DTO record
- `Response` — existing application util
- `Maybe<T>` from CSharpFunctionalExtensions

## [Files]

### New files to create:
```
tests/CashFlow.Consolidation.Tests/GetDailyConsolidationQueryHandlerTests.cs
tests/CashFlow.Consolidation.Tests/InvalidateConsolidationCacheCommandHandlerTests.cs
tests/CashFlow.Consolidation.Tests/UpdateConsolidationCacheCommandHandlerTests.cs
```

### Existing files to modify:
```
src/gateway/CashFlow.Gateway/Program.cs
  → Add "require-admin" and "require-user" authorization policies
  → Configure RoleClaimType = "roles" in JwtBearer options

src/gateway/CashFlow.Gateway/appsettings.json
  → Split transactions-route into transactions-write-route (POST) + transactions-read-route (GET)
  → Assign role-based authorization policies to each route

src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs
  → Add RoleClaimType = "roles" in AddJwtAuthentication
  → Add role-based authorization policies

src/transactions/CashFlow.Transactions.API/Endpoints/Transactions/TransactionEndpoints.cs
  → POST / → .RequireAuthorization("require-admin")
  → GET /   → .RequireAuthorization("require-user")
  → GET /{id} → .RequireAuthorization("require-user")

src/consolidation/CashFlow.Consolidation.API/Extensions/ServiceCollectionExtensions.cs
  → Add RoleClaimType = "roles" in AddJwtAuthentication
  → Add role-based authorization policies

src/consolidation/CashFlow.Consolidation.API/Program.cs
  → GET /consolidation/{date} → .RequireAuthorization("require-user")

README.md
  → Fix "Cache-First (Redis)" → "Cache-First (IMemoryCache)"
  → Fix Fase 5 status: "🔄 Planejado" → "✅ Completo"
  → Fix Consolidation Worker in tree: 🔄 → ✅
  → Add "Requisito x Evidência" table
  → Fix authentication table (remove "Policy default" reference)

docs/architecture/06-architectural-patterns.md
  → Section 2 (Cache-First): Replace Redis with IMemoryCache as chosen solution
  → Update "Alternativas Descartadas": move Redis to descartada, remove IMemoryCache from descartada
  → Update configuration section (remove Redis config, add IMemoryCache TTL config)
  → Section 8 (Circuit Breaker): Remove Redis circuit breaker example, note IMemoryCache is in-process

docs/security/02-authentication-authorization.md
  → Section 4.1 RBAC Model: Replace granular roles with admin/user per ADR-009
  → Section 4.2 Endpoint table: Update roles to admin/user
  → Section 4.3 User profiles: Align with admin/user model
  → Section 2.1 JWT payload example: Update realm_access.roles to ["admin"] or ["user"]

docs/requirements/01-functional-requirements.md
  → UC-03: Replace "cache (Redis)" → "cache (IMemoryCache)"
  → Endpoint tables: Fix paths (/api/transactions → /api/v1/transactions, /consolidation/{date})
  → UC-01/UC-02 roles: Update from transactions:write → admin
  → UC-03/UC-04 roles: Update from consolidation:read/transactions:read → user
```

## [Functions]

### New functions (test methods):

**GetDailyConsolidationQueryHandlerTests.cs:**
- `Handle_ShouldReturnOk_WhenCacheHit()` — mocks cache returning a value
- `Handle_ShouldReturnOk_WhenCacheMissAndDbFound()` — mocks cache miss, repo returns data
- `Handle_ShouldReturnNotFound_WhenCacheMissAndDbNotFound()` — repo returns None
- `Handle_ShouldReturnBadRequest_WhenUserIdIsNull()` — validation phase
- `Handle_ShouldReturnBadRequest_WhenDateIsDefault()` — validation phase
- `Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()` — DB error path
- `Handle_ShouldPopulateCache_WhenCacheMissAndDbFound()` — verifies SetAsync called
- `Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()`
- `Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull()`
- `Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()`

**InvalidateConsolidationCacheCommandHandlerTests.cs:**
- `Handle_ShouldReturnOk_WhenKeysAreValidAndInvalidated()`
- `Handle_ShouldReturnBadRequest_WhenConsolidationKeysIsEmpty()`
- `Handle_ShouldReturnInternalServerError_WhenCacheThrows()`
- `Handle_ShouldSkipInvalidKeyFormat_WhenKeyCannotBeParsed()`
- `Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()`
- `Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()`

**UpdateConsolidationCacheCommandHandlerTests.cs:**
- `Handle_ShouldReturnOk_WhenConsolidationsFoundAndCacheUpdated()`
- `Handle_ShouldReturnOk_WhenSomeKeysNotFoundInDb()`
- `Handle_ShouldReturnBadRequest_WhenConsolidationKeysIsEmpty()`
- `Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()`
- `Handle_ShouldReturnInternalServerError_WhenCacheSetThrows()`
- `Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull()`
- `Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()`
- `Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()`

### Modified functions:

**`ServiceCollectionExtensions.AddJwtAuthentication` (Gateway)**
- Add `RoleClaimType = "roles"` to `TokenValidationParameters`
- Note: Gateway already re-validates JWT; role extraction must be mapped here

**`Program.cs` (Gateway) — `builder.Services.AddAuthorization`**
- Add `options.AddPolicy("require-admin", policy => policy.RequireRole("admin"))`
- Add `options.AddPolicy("require-user", policy => policy.RequireAuthenticatedUser())`
- Keep `"authenticated"` for backward compatibility on health check

**`ServiceCollectionExtensions.AddJwtAuthentication` (Transactions API)**
- Add `RoleClaimType = "roles"` to `TokenValidationParameters`
- Add `services.AddAuthorization(options => {...})` with admin/user policies

**`TransactionEndpoints.MapTransactionEndpoints`**
- Change `POST /` `.RequireAuthorization()` → `.RequireAuthorization("require-admin")`
- Change `GET /{id}` `.RequireAuthorization()` → `.RequireAuthorization("require-user")`
- Change `GET /` `.RequireAuthorization()` → `.RequireAuthorization("require-user")`

**`ServiceCollectionExtensions.AddJwtAuthentication` (Consolidation API)**
- Add `RoleClaimType = "roles"` to `TokenValidationParameters`
- Add `services.AddAuthorization(options => {...})` with user policy

**`Program.cs` (Consolidation API) — endpoint registration**
- Change `.RequireAuthorization()` → `.RequireAuthorization("require-user")`

## [Classes]

### New classes:

**`GetDailyConsolidationQueryHandlerTests`** — `tests/CashFlow.Consolidation.Tests/GetDailyConsolidationQueryHandlerTests.cs`
- Constructor: sets up `Mock<IConsolidationCache>`, `Mock<IConsolidationQueryRepository>`, `Mock<ILogger>`, SUT
- Tests: 10 test methods covering all handler branches

**`InvalidateConsolidationCacheCommandHandlerTests`** — `tests/CashFlow.Consolidation.Tests/InvalidateConsolidationCacheCommandHandlerTests.cs`
- Constructor: sets up `Mock<IConsolidationCache>`, `Mock<ILogger>`, SUT
- Tests: 6 test methods

**`UpdateConsolidationCacheCommandHandlerTests`** — `tests/CashFlow.Consolidation.Tests/UpdateConsolidationCacheCommandHandlerTests.cs`
- Constructor: sets up `Mock<IConsolidationQueryRepository>`, `Mock<IConsolidationCache>`, `Mock<ILogger>`, SUT
- Tests: 8 test methods

## [Dependencies]

No new NuGet packages required. `CashFlow.Consolidation.Tests.csproj` already references:
- `xunit`, `xunit.runner.visualstudio`, `Moq`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
- Project references: `CashFlow.SharedKernel`, `CashFlow.Consolidation.API`, `CashFlow.Consolidation.Worker`

All dependencies for RBAC (`Microsoft.AspNetCore.Authentication.JwtBearer`) are already in use.

## [Testing]

Tests are implemented in `tests/CashFlow.Consolidation.Tests/`.

Validation after each phase:
```bash
dotnet restore
dotnet build
dotnet test
```

Expected final result: 0 failures across all test projects.
- `CashFlow.Transactions.Tests`: 18 tests ✅
- `CashFlow.Transactions.Worker.Tests`: 12 tests ✅
- `CashFlow.Consolidation.Tests`: ~24 new tests ✅
- `CashFlow.Integration.Tests`: 0 tests (no infra available — acceptable)

## [Implementation Order]

Numbered steps in order of execution:

1. **Create `GetDailyConsolidationQueryHandlerTests.cs`** — largest handler, most complex (cache hit/miss paths)
2. **Create `InvalidateConsolidationCacheCommandHandlerTests.cs`** — simpler, in-memory only
3. **Create `UpdateConsolidationCacheCommandHandlerTests.cs`** — DB + cache interactions
4. **Validate: `dotnet test`** — confirm all Consolidation tests pass
5. **Modify `Gateway/Extensions/ServiceCollectionExtensions.cs`** — add `RoleClaimType = "roles"`
6. **Modify `Gateway/Program.cs`** — add `require-admin` + `require-user` authorization policies
7. **Modify `Gateway/appsettings.json`** — split transactions route into write/read with role policies
8. **Modify `Transactions.API/Extensions/ServiceCollectionExtensions.cs`** — add `RoleClaimType` + policies
9. **Modify `Transactions.API/Endpoints/TransactionEndpoints.cs`** — assign role policies per endpoint
10. **Modify `Consolidation.API/Extensions/ServiceCollectionExtensions.cs`** — add `RoleClaimType` + policies
11. **Modify `Consolidation.API/Program.cs`** — assign `require-user` to GET endpoint
12. **Validate: `dotnet build && dotnet test`** — confirm no regressions
13. **Modify `README.md`** — fix all 4 inconsistencies + add Requisito x Evidência table
14. **Modify `docs/architecture/06-architectural-patterns.md`** — Cache-First section: Redis→IMemoryCache
15. **Modify `docs/security/02-authentication-authorization.md`** — RBAC section: align with ADR-009
16. **Modify `docs/requirements/01-functional-requirements.md`** — fix paths + Redis references
17. **Final validate: `dotnet restore && dotnet build && dotnet test`** — all green
