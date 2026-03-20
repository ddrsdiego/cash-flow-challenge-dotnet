# Entendimento — CashFlow.SharedKernel

## Contexto

Projeto de desafio técnico de Arquiteto de Soluções.  
Fases 1–3 (documentação arquitetural, segurança e operações) 100% concluídas.  
Esta sessão inicia a **Fase 4 — Implementação do Código**, começando pelo SharedKernel.

## Tarefa

Criar a estrutura base da solução .NET e o projeto `CashFlow.SharedKernel` com todos os contratos compartilhados entre os demais serviços.

## Objetivo

Fornecer a fundação de código que os projetos `Transactions.API`, `Consolidation.Worker`, `Consolidation.API` e `Gateway` irão referenciar, garantindo consistência de contratos, entidades de domínio, eventos e interfaces.

## Escopo (o que SERÁ feito)

### Estrutura da Solução
```
CashFlow.sln
Directory.Build.props
Directory.Packages.props        → Versões centralizadas de NuGet
global.json
src/
├── CashFlow.SharedKernel/      ← ESTE PROJETO (esta sessão)
├── CashFlow.Transactions.API/  → Stub (próximas sessões)
├── CashFlow.Consolidation.Worker/
├── CashFlow.Consolidation.API/
└── CashFlow.Gateway/
tests/
├── CashFlow.Transactions.Tests/
├── CashFlow.Consolidation.Tests/
└── CashFlow.Integration.Tests/
```

### Conteúdo do SharedKernel
```
src/CashFlow.SharedKernel/
├── Domain/
│   ├── Entities/
│   │   ├── Transaction.cs              → Aggregate root (MongoDB)
│   │   └── DailyConsolidation.cs       → Aggregate do consolidado diário
│   └── Enums/
│       ├── TransactionType.cs          → DEBIT | CREDIT
│       └── Category.cs                 → Sales, Services, Supplies, Utilities, Returns, Other
├── Messages/
│   └── TransactionCreatedEvent.cs      → sealed record com [MessageUrn]
├── DTOs/
│   ├── Requests/
│   │   └── CreateTransactionRequest.cs → record imutável
│   └── Responses/
│       ├── TransactionResponse.cs      → record imutável
│       └── DailyConsolidationResponse.cs
└── Interfaces/
    ├── ITransactionRepository.cs       → Maybe<T> + IReadOnlyCollection<T> + IClientSessionHandle
    ├── IConsolidationRepository.cs     → Maybe<T> + Task (upsert batch)
    ├── IConsolidationCache.cs          → GetAsync / SetAsync / InvalidateAsync
    ├── ICacheInvalidator.cs            → InvalidateAsync(date)
    └── ITransactionalPublisher.cs      → Session + PublishAsync<T>
```

## Fora do Escopo

- Implementações de repositório (MongoDB) → Transactions.API / Consolidation.Worker
- Endpoints HTTP → Transactions.API / Consolidation.API
- Consumers/Workers → Consolidation.Worker
- API Gateway (YARP) → CashFlow.Gateway
- Testes unitários → Fase 5
- Docker Compose → já existe

## Decisões Arquiteturais

### MassTransit + MongoDB Outbox
- **Decisão:** Usar MassTransit como infraestrutura de mensageria (não custom Outbox)
- **Transport:** RabbitMQ (`MassTransit.RabbitMQ`) ao invés de Azure Service Bus
- **Outbox:** `MassTransit.MongoDb` — `AddMongoDbOutbox()` + `UseMongoDbOutbox()`
- **Impacto:** Remove necessidade de `OutboxMessage`, `IOutboxRepository`, `OutboxPublisher` customizados

### Maybe<T>
- **Decisão:** Usar `CSharpFunctionalExtensions` (NuGet) — não implementar custom
- **Versão:** 3.6.0 (mesmo do projeto de referência order-taking)

### Pacotes NuGet
- Somente pacotes públicos (sem pacotes internos Aurora B2B)
- Versões centralizadas via `Directory.Packages.props`

### ITransactionalPublisher
- Mesmo padrão do projeto de referência order-taking
- `IClientSessionHandle Session` + `PublishAsync<T>(T message, ...)`
- Permite handlers usarem a sessão MongoDB da transação MassTransit

## Padrões a Seguir

- `create-entity.md` para entidades
- `entity.cs.template` como referência de implementação
- `guideline-implementacao.md` para interfaces de repositório
- File-scoped namespaces, records imutáveis para DTOs/Events
- `[BsonIgnoreExtraElements]`, `[BsonId]`, `[BsonRepresentation]`, `[BsonElement]`
- Sem primary constructors, sem collection expressions

## Critérios de Aceite

- [ ] `dotnet restore` ✅
- [ ] `dotnet build` ✅ (toda a solução)
- [ ] Entidades seguem `entity.cs.template`
- [ ] Events são `sealed record` com `[MessageUrn]`
- [ ] Interfaces usam `Maybe<T>` de CSharpFunctionalExtensions
- [ ] `Directory.Packages.props` gerencia todas as versões
- [ ] Sem pacotes internos Aurora B2B

## Referências

- Projeto referência: `C:\Users\User\Documents\desenvolvimento\dotnet\order-taking\`
- Documentação arquitetural: `docs/architecture/`
- Requisitos funcionais: `docs/requirements/01-functional-requirements.md`
- Plano de implementação: `docs/plano-implementacao.md`

---
📅 Criado em: 19/03/2026  
🔧 Sessão: Fase 4 — Implementação (Sessão 1)
