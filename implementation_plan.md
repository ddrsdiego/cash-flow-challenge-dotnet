# Implementation Plan — Last-Mile Feedback Remediation

[Overview]
Resolve all 4 remaining feedback items from the technical evaluation to bring the project to full compliance.

This plan addresses four items identified in the final feedback cycle: (1) a test count discrepancy between README (claims 50) and actual code (49 `[Fact]`), (2) absence of any real tests in `CashFlow.Integration.Tests`, (3) `NotImplementedException` code smell in two repository implementations due to fat interface contracts, and (4) hardcoded infrastructure credentials in `appsettings.Development.json` and `appsettings.json` files.

The interface segregation work is the most impactful change—it removes 8 `NotImplementedException` methods by splitting `IRawRequestRepository` and `ITransactionRepository` into scoped sub-interfaces, each consumed only by the component that needs it. The integration tests use a minimal MediatR pipeline (ServiceCollection + real handler + mocked repositories) to validate DI wiring without requiring live infrastructure. Credential hygiene is addressed by emptying hardcoded passwords and documenting the .NET environment variable override pattern in `env.example` and README.

[Types]
One new interface for transaction write operations and one for raw request ingestion are added to SharedKernel.

**New: `ITransactionWriteRepository`** — `src/CashFlow.SharedKernel/Interfaces/ITransactionWriteRepository.cs`
```
namespace CashFlow.SharedKernel.Interfaces;
public interface ITransactionWriteRepository
{
    Task InsertAsync(IEnumerable<Transaction> transactions, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
}
```

**New: `IRawRequestIngestionRepository`** — `src/CashFlow.SharedKernel/Interfaces/IRawRequestIngestionRepository.cs`
```
namespace CashFlow.SharedKernel.Interfaces;
public interface IRawRequestIngestionRepository
{
    Task<Maybe<RawRequest>> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task InsertAsync(RawRequest request, IClientSessionHandle session = null, CancellationToken cancellationToken = default);
}
```

**Modified: `ITransactionRepository`** — extends `ITransactionWriteRepository`; `InsertAsync` is removed from body (inherited).

**Modified: `IRawRequestRepository`** — extends `IRawRequestIngestionRepository`; `GetByIdempotencyKeyAsync` and `InsertAsync` are removed from body (inherited).

[Files]
Twelve files are modified and four new files are created across SharedKernel, test projects, configuration, and documentation.

**New files to create:**
- `src/CashFlow.SharedKernel/Interfaces/ITransactionWriteRepository.cs` — new segregated write-only transaction interface
- `src/CashFlow.SharedKernel/Interfaces/IRawRequestIngestionRepository.cs` — new segregated ingestion-only raw request interface
- `tests/CashFlow.Integration.Tests/TransactionsApiIntegrationTests.cs` — 2 integration tests via MediatR pipeline
- `tests/CashFlow.Integration.Tests/ConsolidationApiIntegrationTests.cs` — 2 integration tests via MediatR pipeline (optional, can merge in single file)

**Existing files to modify:**

*SharedKernel — Interface contracts:*
- `src/CashFlow.SharedKernel/Interfaces/ITransactionRepository.cs` — add `: ITransactionWriteRepository`, remove `InsertAsync` declaration from body
- `src/CashFlow.SharedKernel/Interfaces/IRawRequestRepository.cs` — add `: IRawRequestIngestionRepository`, remove `GetByIdempotencyKeyAsync` and `InsertAsync` declarations from body

*Transactions.Worker — Eliminate NotImplementedException:*
- `src/transactions/CashFlow.Transactions.Worker/Infrastructure/MongoDB/TransactionRepository.cs` — change `ITransactionRepository` → `ITransactionWriteRepository`; remove 3 `throw new NotImplementedException` methods (GetByIdAsync, GetByPeriodAsync, CountByPeriodAsync)
- `src/transactions/CashFlow.Transactions.Worker/Extensions/ServiceCollectionExtensions.cs` — change DI registration from `AddScoped<ITransactionRepository, TransactionRepository>()` → `AddScoped<ITransactionWriteRepository, TransactionRepository>()`
- `src/transactions/CashFlow.Transactions.Worker/Application/UseCases/ProcessTransactionBatch/ProcessTransactionBatchCommandHandler.cs` — change field type and constructor parameter from `ITransactionRepository` → `ITransactionWriteRepository`

*Transactions.API — Eliminate NotImplementedException:*
- `src/transactions/CashFlow.Transactions.API/Infrastructure/MongoDB/RawRequestRepository.cs` — change `IRawRequestRepository` → `IRawRequestIngestionRepository`; remove 5 `throw new NotImplementedException` methods (FindPendingAsync, MarkAsDispatchedAsync, FindOrphanedDispatchedAsync, MarkAsProcessedAsync, GetByBatchIdAsync)
- `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs` — change DI registration from `AddScoped<IRawRequestRepository, RawRequestRepository>()` → `AddScoped<IRawRequestIngestionRepository, RawRequestRepository>()`
- `src/transactions/CashFlow.Transactions.API/Application/UseCases/CreateTransaction/CreateTransactionCommandHandler.cs` — change field type and constructor parameter from `IRawRequestRepository` → `IRawRequestIngestionRepository`

*Tests — 50th test:*
- `tests/CashFlow.Consolidation.Tests/InvalidateConsolidationCacheCommandHandlerTests.cs` — add 1 new `[Fact]` test: `Handle_ShouldCallCacheInvalidateForEachKey_WhenValidKeysProvided`
- `tests/CashFlow.Integration.Tests/CashFlow.Integration.Tests.csproj` — add PackageReference for `MediatR` + `Moq` (already present) + ProjectReferences to `CashFlow.Transactions.API` and `CashFlow.Consolidation.API`

*Credentials hygiene:*
- `src/consolidation/CashFlow.Consolidation.API/appsettings.json` — replace `"Mongo@CashFlow2024!"` and `"RabbitMQ@CashFlow2024!"` with `""` 
- `src/transactions/CashFlow.Transactions.API/appsettings.Development.json` — replace `"Mongo@CashFlow2024!"` and `"RabbitMQ@CashFlow2024!"` with `""`
- `src/transactions/CashFlow.Transactions.Worker/appsettings.Development.json` — replace `"Mongo@CashFlow2024!"` and `"RabbitMQ@CashFlow2024!"` with `""`
- `src/consolidation/CashFlow.Consolidation.Worker/appsettings.Development.json` — replace `"Mongo@CashFlow2024!"`, `"Redis@CashFlow2024!"`, `"RabbitMQ@CashFlow2024!"` with `""`
- `env.example` — add section `# .NET App Overrides` documenting env var keys: `MongoDB__Password`, `RabbitMQ__Password`, `Redis__ConnectionString`
- `README.md` — fix Fase 5 count from "50" narrative to reflect actual 50 (18+20+12), add env vars section in "Execução Local", add mention of integration tests

[Functions]
New interface methods are distributed across 2 new interfaces; 8 `NotImplementedException` throw statements are removed; 1 new test method is added; and 2 integration test classes with 2 methods each are created.

**New functions:**
- `ITransactionWriteRepository.InsertAsync` (interface method, body in existing `Transactions.API/TransactionRepository` and `Transactions.Worker/TransactionRepository`)
- `IRawRequestIngestionRepository.GetByIdempotencyKeyAsync` (interface method)
- `IRawRequestIngestionRepository.InsertAsync` (interface method)
- `InvalidateConsolidationCacheCommandHandlerTests.Handle_ShouldCallCacheInvalidateForEachKey_WhenValidKeysProvided` — test that verifies `IConsolidationCache.InvalidateAsync` is called `Times.Exactly(keys.Count)` after handler processes valid keys
- `TransactionsApiIntegrationTests.CreateTransactionCommand_ShouldReturn202_WhenHandledByMediatRPipeline` — builds ServiceCollection with real MediatR + mocked `IRawRequestIngestionRepository`, sends `CreateTransactionCommand`, asserts `StatusCode == 202`
- `TransactionsApiIntegrationTests.CreateTransactionCommand_ShouldReturn400_WhenAmountIsZero` — same pipeline, asserts validation path returns `StatusCode == 400`
- `ConsolidationApiIntegrationTests.GetDailyConsolidationQuery_ShouldReturn200_WhenHandledByMediatRPipelineWithCacheHit` — builds ServiceCollection with real MediatR + mocked `IConsolidationCache` (hit) + mocked `IConsolidationQueryRepository`, sends query, asserts 200
- `ConsolidationApiIntegrationTests.GetDailyConsolidationQuery_ShouldReturn404_WhenHandledByMediatRPipelineWithCacheMissAndNoData` — same pipeline, cache miss + repo returns None, asserts 404

**Modified functions:**
- `ProcessTransactionBatchCommandHandler` constructor — parameter type `ITransactionRepository` → `ITransactionWriteRepository`
- `CreateTransactionCommandHandler` constructor — parameter type `IRawRequestRepository` → `IRawRequestIngestionRepository`
- `Transactions.Worker.ServiceCollectionExtensions.AddMongoDb` — DI registration type changed
- `Transactions.API.ServiceCollectionExtensions.AddMongoDb` — DI registration type changed

**Removed functions (NotImplementedException stubs):**
- `Transactions.Worker.TransactionRepository.GetByIdAsync` — removed entirely (not part of `ITransactionWriteRepository`)
- `Transactions.Worker.TransactionRepository.GetByPeriodAsync` — removed entirely
- `Transactions.Worker.TransactionRepository.CountByPeriodAsync` — removed entirely
- `Transactions.API.RawRequestRepository.FindPendingAsync` — removed entirely (not part of `IRawRequestIngestionRepository`)
- `Transactions.API.RawRequestRepository.MarkAsDispatchedAsync` — removed entirely
- `Transactions.API.RawRequestRepository.FindOrphanedDispatchedAsync` — removed entirely
- `Transactions.API.RawRequestRepository.MarkAsProcessedAsync` — removed entirely
- `Transactions.API.RawRequestRepository.GetByBatchIdAsync` — removed entirely

[Classes]
Two repository classes are refactored to implement narrower interfaces; two integration test classes are added.

**New classes:**
- `TransactionsApiIntegrationTests` — `tests/CashFlow.Integration.Tests/TransactionsApiIntegrationTests.cs`; xUnit test class; no constructor dependencies; uses `ServiceCollection` internally per test; 2 `[Fact]` methods
- `ConsolidationApiIntegrationTests` — `tests/CashFlow.Integration.Tests/ConsolidationApiIntegrationTests.cs`; same pattern; 2 `[Fact]` methods

**Modified classes:**
- `Transactions.Worker.TransactionRepository` — implements `ITransactionWriteRepository` instead of `ITransactionRepository`; 3 stub methods removed; `InsertAsync` implementation unchanged
- `Transactions.API.RawRequestRepository` — implements `IRawRequestIngestionRepository` instead of `IRawRequestRepository`; 5 stub methods removed; `GetByIdempotencyKeyAsync` and `InsertAsync` implementations unchanged
- `ProcessTransactionBatchCommandHandler` — field `_transactionRepository` type `ITransactionRepository` → `ITransactionWriteRepository`; constructor parameter updated accordingly
- `CreateTransactionCommandHandler` — field `_rawRequestRepository` type `IRawRequestRepository` → `IRawRequestIngestionRepository`; constructor parameter updated accordingly

[Dependencies]
One new package reference is required for the integration test project.

- `tests/CashFlow.Integration.Tests/CashFlow.Integration.Tests.csproj`: add `<PackageReference Include="MediatR" />` (version from `Directory.Packages.props`) — needed to instantiate `IMediator` in pipeline tests
- `tests/CashFlow.Integration.Tests/CashFlow.Integration.Tests.csproj`: add `<ProjectReference>` to `CashFlow.Transactions.API.csproj` — to access `CreateTransactionCommand`, `CreateTransactionCommandHandler`
- `tests/CashFlow.Integration.Tests/CashFlow.Integration.Tests.csproj`: add `<ProjectReference>` to `CashFlow.Consolidation.API.csproj` — to access `GetDailyConsolidationQuery`, `GetDailyConsolidationQueryHandler`
- No changes to production project dependencies

[Testing]
Validation requires `dotnet build` and `dotnet test` passing with exactly 53 `[Fact]` tests after changes.

**Test count after implementation:**
| Project | Before | After |
|---------|--------|-------|
| `CashFlow.Transactions.Tests` | 18 | 18 (unchanged) |
| `CashFlow.Consolidation.Tests` | 19 | 20 (+1 InvalidateCache test) |
| `CashFlow.Transactions.Worker.Tests` | 12 | 12 (unchanged) |
| `CashFlow.Integration.Tests` | 0 | 4 (2 Transactions + 2 Consolidation) |
| **Total** | **49** | **53** |

**README update**: Change "50 testes (Transactions 18 + Consolidation 20 + Worker 12)" to "53 testes: unitários (18+20+12=50) + integração (4)".

**Validation sequence:**
1. `dotnet restore CashFlow.sln` — no new external packages except `MediatR` in integration test project
2. `dotnet build CashFlow.sln` — verify 0 errors, 0 warnings related to NotImplementedException
3. `dotnet test CashFlow.sln` — all 53 tests green
4. PowerShell verify: `Select-String -Pattern '\[Fact\]' -Recurse -Path tests\ -Filter '*.cs' | Measure-Object` → 53

[Implementation Order]
Changes are ordered to maintain a compilable solution at every step—interfaces first, then implementations, then DI, then handlers, then tests.

1. **Create `ITransactionWriteRepository`** in SharedKernel — no breaking changes yet
2. **Create `IRawRequestIngestionRepository`** in SharedKernel — no breaking changes yet
3. **Extend `ITransactionRepository : ITransactionWriteRepository`** and remove `InsertAsync` from body — compile check: API `TransactionRepository` still satisfies `ITransactionRepository` via `ITransactionWriteRepository`
4. **Extend `IRawRequestRepository : IRawRequestIngestionRepository`** and remove shared methods from body — same pattern
5. **Refactor `Transactions.Worker.TransactionRepository`** — change implements clause, delete 3 stub methods — build verifies no missing interface members
6. **Refactor `Transactions.API.RawRequestRepository`** — change implements clause, delete 5 stub methods
7. **Update `ProcessTransactionBatchCommandHandler`** — change field type to `ITransactionWriteRepository`
8. **Update `CreateTransactionCommandHandler`** — change field type to `IRawRequestIngestionRepository`
9. **Update `Transactions.Worker.ServiceCollectionExtensions.AddMongoDb`** — change DI registration
10. **Update `Transactions.API.ServiceCollectionExtensions.AddMongoDb`** — change DI registration
11. **Run `dotnet build`** — verify zero errors before proceeding to tests
12. **Add 50th test** to `InvalidateConsolidationCacheCommandHandlerTests.cs`
13. **Update `CashFlow.Integration.Tests.csproj`** — add MediatR package + project references
14. **Create `TransactionsApiIntegrationTests.cs`** with 2 `[Fact]` methods
15. **Create `ConsolidationApiIntegrationTests.cs`** with 2 `[Fact]` methods
16. **Credential cleanup** — empty passwords in 4 appsettings files
17. **Update `env.example`** — add .NET env var override section
18. **Update `README.md`** — fix test count (50 unit + 4 integration), add env var note
19. **Run `dotnet test`** — verify all 53 tests pass
20. **Run `dotnet build` final** — confirm zero warnings/errors
