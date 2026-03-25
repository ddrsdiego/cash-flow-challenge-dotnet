# Implementation Plan

[Overview]
Refactor `BatcherBackgroundService` to remove direct repository dependencies and delegate all business logic to a MediatR command dispatched via `IServiceScopeFactory`.

The current `BatcherBackgroundService` (Singleton) directly injects `IRawRequestRepository` and `IDistributedLockRepository` (both Scoped), causing a DI lifetime violation at startup. The user's requirement is that the service should not know about repositories — it should only create a DI scope per cycle and dispatch a MediatR command. That command (and its handler) encapsulates the full logic: acquire distributed lock, sweep orphaned requests, find pending items, mark as dispatched, publish the event. Existing artifacts (`DispatchTransactionBatchCommand`, `DispatchTransactionBatchCommandHandler`, `DispatchTransactionBatchErrors`, `DispatchTransactionBatchLog`, `TransactionBatchReadyEvent`) will be reused and adapted.

[Types]
No new types are introduced; existing `DispatchTransactionBatchCommand` record signature changes to carry context instead of pre-fetched data.

Current signature:
```csharp
public record DispatchTransactionBatchCommand(
    string TracerId,
    IReadOnlyList<string> RawRequestIds,
    string BatchId) : IRequest<Response>;
```

New signature (handler fetches data internally):
```csharp
public record DispatchTransactionBatchCommand(
    string TracerId,
    string InstanceId,
    int BatchSize,
    int LockTtlSeconds,
    int SweepThresholdMinutes) : IRequest<Response>;
```

- `TracerId` — correlation ID for observability
- `InstanceId` — unique identifier of the running worker pod (used for distributed lock)
- `BatchSize` — max number of pending requests to fetch per cycle (from configuration)
- `LockTtlSeconds` — TTL for the distributed lock (from configuration)
- `SweepThresholdMinutes` — age threshold to recover orphaned dispatched requests (from configuration)

[Files]
Two existing files are modified; no new files are created; no files are deleted.

**Modified files:**

1. `src/transactions/CashFlow.Transactions.Worker/Workers/BatcherBackgroundService.cs`
   - Remove fields: `_mediator`, `_rawRequestRepository`, `_distributedLockRepository`
   - Add field: `_scopeFactory` (`IServiceScopeFactory`)
   - Constructor receives only: `IServiceScopeFactory`, `IConfiguration`, `ILogger<BatcherBackgroundService>`
   - `ExecuteAsync` loop: create an `AsyncServiceScope` per cycle, resolve `IMediator`, send `DispatchTransactionBatchCommand`
   - Remove private methods: `PollAndDispatchAsync`, `SweepOrphanedDispatchedAsync` (logic moves to handler)

2. `src/transactions/CashFlow.Transactions.Worker/Application/UseCases/DispatchTransactionBatch/DispatchTransactionBatchCommand.cs`
   - Replace `RawRequestIds` and `BatchId` parameters with `InstanceId`, `BatchSize`, `LockTtlSeconds`, `SweepThresholdMinutes`

3. `src/transactions/CashFlow.Transactions.Worker/Application/UseCases/DispatchTransactionBatch/DispatchTransactionBatchCommandHandler.cs`
   - Add `IDistributedLockRepository` to constructor (already present via existing DI registration)
   - Phase 1: validate `TracerId`, `InstanceId`
   - Phase 2:
     - `AcquireDistributedLockAsync` — if lock not acquired (another instance holds it) → return `Response.NoContent()` silently
     - `SweepOrphanedRawRequestsAsync` — recover orphaned dispatched items older than threshold
     - `FindPendingRawRequestsAsync` — if empty → return `Response.NoContent()` silently
   - Phase 3 (reuse existing): `DispatchBatchAsync` — mark as dispatched + publish `TransactionBatchReadyEvent`

4. `src/transactions/CashFlow.Transactions.Worker/Application/UseCases/DispatchTransactionBatch/DispatchTransactionBatchErrors.cs`
   - Add: `LockNotAcquired` (informational, no retry needed — treated as no-op)

5. `src/transactions/CashFlow.Transactions.Worker/Application/UseCases/DispatchTransactionBatch/DispatchTransactionBatchLog.cs`
   - Add log entries: `LockNotAcquired`, `SweepingOrphanedRequests`, `OrphanedRequestsRecovered`, `NoPendingRequests`, `AcquiredLock`

[Functions]
Handler gains new private methods; BackgroundService loses private methods.

**New private methods in `DispatchTransactionBatchCommandHandler`:**

- `AcquireDistributedLockAsync(DispatchTransactionBatchCommand, CancellationToken) → Task<Result<DistributedLock, Response>>`
  - Calls `_distributedLockRepository.TryAcquireAsync(LockId, request.InstanceId, request.LockTtlSeconds, ct)`
  - If `Maybe.HasNoValue` → returns `Result.Failure` with a "lock not acquired" no-op response
  - On exception → returns `Result.Failure` with 500

- `SweepOrphanedRawRequestsAsync(DispatchTransactionBatchCommand, CancellationToken) → Task`
  - Calls `_rawRequestRepository.FindOrphanedDispatchedAsync(request.SweepThresholdMinutes, ct)`
  - Logs count if > 0; exceptions caught and logged (non-fatal, does not abort the cycle)

- `FindPendingRawRequestsAsync(DispatchTransactionBatchCommand, CancellationToken) → Task<Result<IReadOnlyCollection<RawRequest>, Response>>`
  - Calls `_rawRequestRepository.FindPendingAsync(request.BatchSize, ct)`
  - If count == 0 → returns `Result.Failure` with a "no pending" no-op response
  - On exception → returns `Result.Failure` with 500

**Modified `DispatchBatchAsync`:**
  - No longer receives `RawRequestIds` and `BatchId` from the command
  - Receives them from the result of `FindPendingRawRequestsAsync`
  - Generates `batchId = Guid.NewGuid().ToString("N")` internally
  - Calls `_rawRequestRepository.MarkAsDispatchedAsync` + `_bus.Publish(TransactionBatchReadyEvent)`

**Removed from `BatcherBackgroundService`:**
- `PollAndDispatchAsync` — logic moved to handler
- `SweepOrphanedDispatchedAsync` — logic moved to handler

**Modified `BatcherBackgroundService.ExecuteAsync`:**
```
while (!stoppingToken.IsCancellationRequested)
{
    using var scope = _scopeFactory.CreateAsyncScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var tracerId = Guid.NewGuid().ToString("N");
    var command = new DispatchTransactionBatchCommand(tracerId, _instanceId, _batchSize, _lockTtlSeconds, _sweepThresholdMinutes);
    var response = await mediator.Send(command, stoppingToken);
    if (response.IsFailure) log warning;
    await Task.Delay(_delayOnEmptyMs, stoppingToken);
}
```

[Classes]
Only existing classes are modified; no new classes are introduced.

- `BatcherBackgroundService` — simplified to Singleton-safe service with only `IServiceScopeFactory`
- `DispatchTransactionBatchCommandHandler` — expanded to include full polling/lock/sweep/dispatch logic

[Dependencies]
No new NuGet packages required; all interfaces are already registered in `ServiceCollectionExtensions.AddMongoDb`.

- `IDistributedLockRepository` — already registered as Scoped in `ServiceCollectionExtensions.AddMongoDb`; handler resolves it via constructor injection within the scoped lifetime created by `IServiceScopeFactory`
- `IServiceScopeFactory` — built-in .NET singleton, safe to inject into `BackgroundService`

[Testing]
No existing tests cover `BatcherBackgroundService` or `DispatchTransactionBatchCommandHandler` — if test projects exist for the Worker, verify they still compile after the command signature change.

Run after implementation:
1. `dotnet restore`
2. `dotnet build`
3. `dotnet test`

[Implementation Order]
Changes must be applied in dependency order to keep the solution buildable at each step.

1. **Modify `DispatchTransactionBatchCommand.cs`** — change record signature (remove `RawRequestIds`/`BatchId`, add `InstanceId`/`BatchSize`/`LockTtlSeconds`/`SweepThresholdMinutes`)
2. **Modify `DispatchTransactionBatchLog.cs`** — add new log entries needed by the updated handler
3. **Modify `DispatchTransactionBatchErrors.cs`** — add `LockNotAcquired` no-op helper if needed
4. **Modify `DispatchTransactionBatchCommandHandler.cs`** — add `IDistributedLockRepository`, implement Phase 2 private methods (`AcquireDistributedLockAsync`, `SweepOrphanedRawRequestsAsync`, `FindPendingRawRequestsAsync`), adapt `DispatchBatchAsync` to generate `batchId` internally
5. **Modify `BatcherBackgroundService.cs`** — remove repository fields, inject `IServiceScopeFactory`, replace `PollAndDispatchAsync`/`SweepOrphanedDispatchedAsync` with scope-based mediator dispatch
6. **Run `dotnet restore && dotnet build && dotnet test`** — validate the solution compiles and all tests pass
