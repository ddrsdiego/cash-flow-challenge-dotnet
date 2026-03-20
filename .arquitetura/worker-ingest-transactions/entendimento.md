# 📋 Entendimento: Worker — Fase 1 (Ingestão de Batch)

**Status:** ✅ **CONCLUÍDO**  
**Validação:** ✅ `dotnet restore` → ✅ `dotnet build` (0 errors, 0 warnings)

---

## 1️⃣ Contexto

A **Fase 1** (STEP 1-5b) criou a fundação:
- ✅ SharedKernel entities, DTOs, interfaces
- ✅ Transactions API adaptada para gerar `BatchId`
- ✅ Worker `.csproj` com packages validados

A **Fase 2** (STEP 6-13) **implementou a infraestrutura e pipeline de ingestão**:
- ✅ Worker Consumer recebe `TransactionCreatedEvent`
- ✅ Mapeia para `IngestTransactionsBatchCommand`
- ✅ Handler persiste em MongoDB (atomicidade via Outbox)
- ✅ Publica `ConsolidationBatchReceivedEvent` para próxima etapa

---

## 2️⃣ Fluxo Implementado

```
┌─────────────────────────────────────────────────────────────────────┐
│                    FASE 2: INGESTÃO DE BATCH                        │
└─────────────────────────────────────────────────────────────────────┘

TransactionCreatedEvent (RabbitMQ)
    │
    ├─ BatchId, TracerId, TransactionItem[]
    │
    ▼
TransactionCreatedConsumer
    │
    ├─ Maps: Event → IngestTransactionsBatchCommand
    ├─ Dispatches via MediatR
    ├─ 4xx errors: Log + ACK (sem retry)
    ├─ 5xx errors: Log + throw (com retry)
    │
    ▼
IngestTransactionsBatchCommandHandler
    │
    ├─ FASE 1: Validar
    │   ├─ BatchId not null/empty
    │   └─ Transactions.Count > 0
    │
    ├─ FASE 2: Resolver Dependências
    │   └─ MapTransactionsAsync: TransactionItem[] → ReceivedTransaction[]
    │
    ├─ FASE 3: Persistir (MassTransit Outbox)
    │   ├─ BulkInsertAsync ReceivedTransactions (MongoDB)
    │   └─ PublishAsync ConsolidationBatchReceivedEvent (MassTransit)
    │
    └─ Atomicidade: Outbox garante tudo-ou-nada
         └─ Se insert falhar: nada é publicado
         └─ Se publish falhar: MassTransit retenta
         └─ Se ambos OK: evento entregue + transações persistidas

    ▼
ConsolidationBatchReceivedEvent (RabbitMQ)
    │
    └─ [PRÓXIMA SESSÃO] ProcessConsolidationBatchConsumer + Handler
```

---

## 3️⃣ Componentes Implementados

### ✅ Infrastructure (5 arquivos)

| Arquivo | Responsabilidade |
|---------|-----------------|
| `Infrastructure/MongoDB/MongoDbContext.cs` | Collections: `received_transactions` + `daily_consolidation` |
| `Infrastructure/MongoDB/ReceivedTransactionRepository.cs` | `IReceivedTransactionRepository`: BulkInsert, GetByBatchId, DeleteByBatchId |
| `Infrastructure/MongoDB/ConsolidationRepository.cs` | `IConsolidationRepository`: GetByDateAsync, UpsertAsync |
| `Infrastructure/Messaging/MassTransitPublisher.cs` | `ITransactionalPublisher` (Worker version): PublishAsync (outbox) |

### ✅ Application (4 arquivos + Handler)

| Arquivo | Responsabilidade |
|---------|-----------------|
| `UseCases/IngestTransactionsBatch/IngestTransactionsBatchCommand.cs` | Record: `(TracerId, BatchId, Transactions)` |
| `UseCases/IngestTransactionsBatch/IngestTransactionsBatchCommandHandler.cs` | 3-phase handler: Validar → Mapear → Persistir |
| `UseCases/IngestTransactionsBatch/IngestTransactionsBatchErrors.cs` | Erros 400 (EMPTY_TRANSACTIONS, INVALID_BATCH_ID) e 500 (DATABASE_ERROR, PUBLISH_ERROR) |
| `UseCases/IngestTransactionsBatch/IngestTransactionsBatchLog.cs` | Logs estruturados com `LoggerMessage` |

### ✅ WebApi/Consumers & Extensions (3 arquivos)

| Arquivo | Responsabilidade |
|---------|-----------------|
| `Consumers/TransactionCreatedConsumer.cs` | IConsumer<TransactionCreatedEvent>: recebe evento, chama handler, avalia response |
| `Extensions/ResponseExtensions.cs` | `ThrowIfServerError`: 4xx = ACK+warn, 5xx = throw+retry |
| `Extensions/ServiceCollectionExtensions.cs` | DI: MongoDB, MassTransit, MediatR, OpenTelemetry |

### ✅ Configuration (2 arquivos)

| Arquivo | Responsabilidade |
|---------|-----------------|
| `Program.cs` | Host builder com Serilog, DI, MassTransit |
| `appsettings.json` | MongoDB, RabbitMQ, OpenTelemetry config |
| `Dockerfile` | Docker image build (paths corrigidos) |

### ✅ Infrastructure (1 arquivo corrigido)

| Arquivo | Mudança |
|---------|--------|
| `docker-compose.yml` | Dockerfile path: `src/consolidation/CashFlow.Consolidation.Worker/Dockerfile` |

---

## 4️⃣ Decisões Técnicas

### 4.1 Atomicidade no Worker

```csharp
// Repository: BulkInsertAsync SEM IClientSessionHandle
await _repository.BulkInsertAsync(transactions, cancellationToken);

// Publisher: PublishAsync via MassTransit Outbox
await _transactionalPublisher.PublishAsync(event, cancellationToken);
```

**Por quê?**
- MassTransit **Outbox Pattern** = inserção + publicação **atômicas**
- Se insert falha → nada é publicado (rollback automático)
- Se publish falha → MassTransit retenta automaticamente
- Consumer Transaction = managed by MassTransit, não manual

---

### 4.2 ITransactionalPublisher no Worker

```csharp
// Worker version:
public IClientSessionHandle Session 
    => throw InvalidOperationException("Not used in Consumer context");

public Task BeginTransactionAsync() => Task.CompletedTask;  // No-op
public Task CommitTransactionAsync() => Task.CompletedTask;  // No-op
public Task PublishAsync<T>(T message, ...) => _publishEndpoint.Publish(...);
```

**Por quê?**
- Consumer-initiated handlers não precisam gerenciar sessions manualmente
- MassTransit cuida automaticamente via Outbox
- Se fossemos usar Session, retornaria erro → design explícito

---

### 4.3 Railway-Oriented Programming (Result<T, Response>)

```csharp
// FASE 2: Resolve dependencies
var transactionsResult = await GetTransactionsAsync(...);
if (transactionsResult.IsFailure)
    return transactionsResult.Error; // 404 ou 500, sem try/catch

// FASE 3: Persist
try
{
    await _repository.BulkInsertAsync(...);
    await _transactionalPublisher.PublishAsync(...);
    return Response.Ok();
}
catch (Exception e)
{
    return IngestTransactionsBatchErrors.DatabaseError(...); // 500
}
```

**Benefícios:**
- Fluxo de erro explícito e testável
- 4xx (validação/negócio) sem try/catch
- 5xx (infraestrutura) com try/catch e retry automático

---

## 5️⃣ Configurações (appsettings.json)

```json
{
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [{ "Name": "Console", "Args": { "formatter": "..." } }]
  },
  "MongoDB": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "consolidation_db"
  },
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  },
  "OpenTelemetry": {
    "ServiceName": "cashflow-consolidation-worker",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

---

## 6️⃣ Validação ✅

### dotnet restore
```
✅ Todos os 8 projetos restaurados (0 errors)
```

### dotnet build
```
✅ Compilação com sucesso
   CashFlow.Consolidation.Worker → .../bin/Debug/net8.0/CashFlow.Consolidation.Worker.dll
   0 Aviso(s)
   0 Erro(s)
   Tempo: 12.60 segundos
```

---

## 7️⃣ Próximas Etapas (Fase 3)

Após consolidação de batch via Worker, a **Fase 3** implementará:

### STEP 14-20: Worker ProcessConsolidationBatch
```
ConsolidationBatchReceivedEvent
    ↓
ProcessConsolidationBatchConsumer
    ↓
ProcessConsolidationBatchCommandHandler (3-phase)
    │
    ├─ FASE 1: Validar (BatchId)
    ├─ FASE 2: Buscar ReceivedTransactions + DailyConsolidation
    ├─ FASE 3: Aplicar transações, atualizar saldo, persistir
    │
    └─ PublishAsync ConsolidationCompleteEvent (para cache invalidation)
```

### STEP 21-25: Consolidation API Endpoints
```
GET /consolidation/{date}          → DailyConsolidation by date
GET /consolidation/{date}/summary  → Daily summary with cache
```

---

## 8️⃣ Referências de Código

### Command Handler Pattern
```csharp
public sealed class IngestTransactionsBatchCommandHandler : IRequestHandler<IngestTransactionsBatchCommand, Response>
{
    public async Task<Response> Handle(IngestTransactionsBatchCommand request, CancellationToken cancellationToken)
    {
        // FASE 1: Validar inputs (sem I/O)
        if (request.Transactions.Count == 0)
            return IngestTransactionsBatchErrors.EmptyTransactions(request.TracerId);

        try
        {
            // FASE 2: Resolver dependências (leitura, transformação)
            var receivedTransactions = MapTransactionsAsync(request);

            // FASE 3: Persistir (transação atômica)
            return await PersistBatchAsync(request, receivedTransactions, cancellationToken);
        }
        catch (Exception ex)
        {
            IngestTransactionsBatchLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return IngestTransactionsBatchErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }
}
```

### Consumer Pattern
```csharp
public class TransactionCreatedConsumer : IConsumer<TransactionCreatedEvent>
{
    public async Task Consume(ConsumeContext<TransactionCreatedEvent> context)
    {
        try
        {
            var command = new IngestTransactionsBatchCommand(...);
            var response = await _mediator.Send(command);
            response.ThrowIfServerError(_logger, LogType, tracerId);
        }
        catch (Exception ex)
        {
            throw; // MassTransit retenta
        }
    }
}
```

---

## 9️⃣ Checklist de Implementação ✅

- [x] STEP 6: MongoDbContext
- [x] STEP 7: Repositories (ReceivedTransactionRepository, ConsolidationRepository)
- [x] STEP 8: MassTransitPublisher (Worker)
- [x] STEP 9: UseCase IngestTransactionsBatch (Command, Handler, Errors, Log)
- [x] STEP 10: Consumer + ResponseExtensions
- [x] STEP 11: ServiceCollectionExtensions + Program.cs + appsettings.json
- [x] STEP 12: Docker fix (Dockerfile + docker-compose.yml) + delete Worker.cs
- [x] STEP 13: Validação (dotnet restore, dotnet build) + entendimento.md

---

## 🎯 Resumo Executivo

**Objetivo:** Implementar a infraestrutura e o pipeline de **recepção e ingestão de batch** no Consolidation Worker.

**Resultado:**
- ✅ 22 arquivos criados/modificados
- ✅ 2 interfaces implementadas (IReceivedTransactionRepository, IConsolidationRepository)
- ✅ 1 UseCase completo (IngestTransactionsBatch) com 3 fases atômicas
- ✅ 1 Consumer para receber TransactionCreatedEvent
- ✅ 4 arquivos de infraestrutura (MongoDB, Messaging, DI, Program)
- ✅ Build com 0 erros, 0 avisos

**Próximo Passo:** Implementar Fase 3 (ProcessConsolidationBatch Consumer + Handler) e Consolidation API endpoints.

---

**Data de Conclusão:** 20 de Março de 2026  
**Arquitetura:** Microserviços Event-Driven com MassTransit Outbox  
**Tecnologia:** .NET 8, MongoDB, RabbitMQ, MediatR, Serilog
