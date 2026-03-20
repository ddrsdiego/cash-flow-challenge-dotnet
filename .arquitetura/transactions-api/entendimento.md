# Entendimento — CashFlow.Transactions.API

## Contexto

Continuação do desafio técnico de Arquiteto de Soluções.  
Fase 4.1 (SharedKernel) concluída com `dotnet build` ✅  
Esta sessão implementa a **Fase 4.2 — Transactions API** completa.

## Tarefa

Implementar o serviço `CashFlow.Transactions.API` com:
- Endpoints HTTP para criação e consulta de transações financeiras
- Persistência em MongoDB com padrão do SharedKernel (`ITransactionRepository`)
- Publicação de eventos via MassTransit + MongoDB Outbox (atomicidade garantida)
- Autenticação JWT (extração de `UserId` do token)
- Observabilidade com OpenTelemetry
- Validação com FluentValidation

## Objetivo

Ao final desta sessão:
- `POST /api/v1/transactions` persiste a transação no MongoDB e publica `TransactionCreatedEvent` via Outbox
- `GET /api/v1/transactions/{id}` retorna uma transação por ID
- `GET /api/v1/transactions` lista transações com paginação
- Solução compila sem erros e `dotnet test` passa

## Escopo (o que SERÁ feito)

### SharedKernel — Adição
```
src/CashFlow.SharedKernel/
└── Application/
    └── Utils/
        └── Response.cs         ← Padrão de resposta (Response, ErrorContent, ErrorResponse, ErrorDetail, builders)
```

### Transactions API — Estrutura Completa
```
src/CashFlow.Transactions.API/
├── CashFlow.Transactions.API.csproj             ← Atualizar com novos pacotes NuGet
├── appsettings.json                             ← MongoDB, RabbitMQ, Keycloak, OpenTelemetry
├── appsettings.Development.json                 ← Overrides para dev local
├── Program.cs                                   ← Bootstrap completo
│
├── Application/
│   └── UseCases/
│       ├── CreateTransaction/
│       │   ├── CreateTransactionCommand.cs           ← record + IRequest<Response>
│       │   ├── CreateTransactionCommandHandler.cs    ← 3 fases: Validate → Resolve → Persist
│       │   ├── CreateTransactionErrors.cs            ← Erros tipados (400/500)
│       │   └── CreateTransactionLog.cs               ← LoggerMessage source generator
│       ├── GetTransactionById/
│       │   ├── GetTransactionByIdQuery.cs
│       │   ├── GetTransactionByIdQueryHandler.cs
│       │   ├── GetTransactionByIdErrors.cs
│       │   └── GetTransactionByIdLog.cs
│       └── ListTransactions/
│           ├── ListTransactionsQuery.cs
│           ├── ListTransactionsQueryHandler.cs
│           └── ListTransactionsLog.cs
│
├── Infrastructure/
│   ├── MongoDB/
│   │   ├── MongoDbContext.cs                    ← IMongoDatabase, coleção transactions
│   │   └── TransactionRepository.cs            ← Implementa ITransactionRepository
│   └── Messaging/
│       └── MassTransitPublisher.cs              ← Implementa ITransactionalPublisher
│
├── Endpoints/
│   └── TransactionEndpoints.cs                 ← Minimal API route registration
│
├── Validators/
│   └── CreateTransactionRequestValidator.cs    ← FluentValidation rules
│
└── Extensions/
    └── ServiceCollectionExtensions.cs          ← DI registration helpers
```

## Fora do Escopo

- Consolidation Worker, Consolidation API, API Gateway
- Testes unitários (Fase 5)
- Configuração do Keycloak realm (já existe `infra/config/keycloak/cashflow-realm.json`)

## Padrão de Resposta (projeto referência: payment-gateway-challenge-dotnet)

```csharp
// Response em Application/Utils/Response.cs (SharedKernel)
// Commands → IRequest<Response>
// Handlers → Task<Response>
// Métodos privados → Result<T, Response> (Railway-Oriented)

// Exemplo Command:
public record CreateTransactionCommand(
    string TracerId,
    string UserId,
    TransactionType Type,
    decimal Amount,
    string Description,
    Category Category,
    DateTime Date) : IRequest<Response>;

// Exemplo Errors:
public static Response InvalidAmount(string tracerId) =>
    Response.Builder()
        .WithRequestId(tracerId)
        .WithStatusCode(StatusCodes.Status400BadRequest)
        .WithErrorResponse(
            ErrorResponse.Builder()
                .WithInstance("/CreateTransaction")
                .WithTraceId(tracerId)
                .WithError("INVALID_AMOUNT", "INVALID_AMOUNT", "Amount must be greater than 0")
                .Build())
        .Build();

// Endpoint Minimal API:
var response = await mediator.Send(command, ct);
return Results.Json(
    response.IsSuccess ? response.Data : response.ErrorContent?.ErrorResponse,
    statusCode: response.StatusCode);
```

## Endpoints

| Método | Rota | Auth | Status Success | Descrição |
|--------|------|------|----------------|-----------|
| POST | /api/v1/transactions | JWT | 201 Created | Criar lançamento |
| GET | /api/v1/transactions/{id} | JWT | 200 OK | Buscar por ID |
| GET | /api/v1/transactions | JWT | 200 OK | Listar com paginação |

**Query params (GET /api/v1/transactions):** `startDate`, `endDate`, `type?` (Debit/Credit), `page=1`, `pageSize=20`

## Fluxo: POST /api/v1/transactions

```
JWT Request
    │
    ├── [Endpoint] → extrai UserId do claim "sub" + TracerId de HttpContext.TraceIdentifier
    │
    └── CreateTransactionCommandHandler
         │
         ├── FASE 1: Validar inputs
         │         → amount > 0, date não futura, description não vazia
         │         → Retorno antecipado: Response 400
         │
         ├── FASE 2: Construir entidade
         │         → new Transaction { UserId, Type, Amount, Description, Category, Date }
         │         → (sem I/O nesta fase)
         │
         └── FASE 3: PersistTransactionAsync()
                  ├── session = await _mongoClient.StartSessionAsync()
                  ├── session.StartTransaction()
                  ├── await _repository.InsertAsync(transaction, session)   ← MongoDB
                  ├── await _transactionalPublisher.PublishAsync(            ← MassTransit Outbox
                  │       new TransactionCreatedEvent(...))
                  ├── await session.CommitTransactionAsync()
                  └── Retorna Response.Created(TransactionResponse)
```

## Atomicidade: MassTransit MongoDB Outbox

- `AddMongoDbOutbox()` registrado no `AddMassTransit()`
- Session MongoDB gerenciada manualmente no handler HTTP (Cenário B da guideline)
- `MassTransitPublisher` expõe a session e delega publish para `IPublishEndpoint`
- Após commit: background service (MassTransit Outbox Delivery) publica ao RabbitMQ

## Pacotes NuGet (adicionados ao .csproj)

| Pacote | Versão | Uso |
|--------|--------|-----|
| MassTransit | 8.2.5 | Message bus |
| MassTransit.MongoDb | 8.2.5 | MongoDB Outbox |
| MassTransit.RabbitMQ | 8.2.5 | RabbitMQ transport |
| MediatR | 12.5.0 | CQRS |
| MongoDB.Driver | 2.28.0 | MongoDB persistence |
| FluentValidation.AspNetCore | 11.3.0 | Request validation |
| CSharpFunctionalExtensions | 3.6.0 | Result/Maybe types |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.0 | JWT auth |
| OpenTelemetry.Extensions.Hosting | 1.8.1 | OTel |
| OpenTelemetry.Instrumentation.AspNetCore | 1.8.1 | OTel HTTP |
| OpenTelemetry.Instrumentation.Http | 1.8.1 | OTel HTTP client |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.8.1 | OTLP exporter |

## Erros Mapeados

| UseCase | Código | Status | Retry? |
|---------|--------|--------|--------|
| CreateTransaction | INVALID_AMOUNT | 400 | ❌ |
| CreateTransaction | INVALID_DATE | 400 | ❌ |
| CreateTransaction | EMPTY_DESCRIPTION | 400 | ❌ |
| CreateTransaction | DATABASE_ERROR | 500 | ✅ |
| CreateTransaction | UNEXPECTED_ERROR | 500 | ✅ |
| GetTransactionById | TRANSACTION_NOT_FOUND | 404 | ❌ |
| GetTransactionById | DATABASE_ERROR | 500 | ✅ |
| ListTransactions | DATABASE_ERROR | 500 | ✅ |

## Critérios de Aceite

- [ ] `POST /api/v1/transactions` retorna `201 Created` com `TransactionResponse`
- [ ] `GET /api/v1/transactions/{id}` retorna `200 OK` ou `404 Not Found`
- [ ] `GET /api/v1/transactions` retorna `200 OK` com `PagedResult<TransactionResponse>`
- [ ] MongoDB persiste corretamente na coleção `transactions`
- [ ] MassTransit publica `TransactionCreatedEvent` ao RabbitMQ via Outbox
- [ ] FluentValidation rejeita requests inválidas com 400
- [ ] JWT obrigatório em todos os endpoints
- [ ] `dotnet restore` ✅ → `dotnet build` ✅ → `dotnet test` ✅

## Referências

- Projeto referência de padrão Response: `C:\Users\User\OneDrive\Desenvolvimento\dotnet\payment-gateway-challenge-dotnet\src\PaymentGateway.Api\Application\Utils\Response.cs`
- SharedKernel (interfaces e entidades): `src/CashFlow.SharedKernel/`
- Documentação arquitetural: `docs/architecture/03-component-transactions.md`
- Plano de implementação: `docs/plano-implementacao.md`

---
📅 Criado em: 19/03/2026  
🔧 Sessão: Fase 4 — Implementação (Sessão 2 — Transactions API)
