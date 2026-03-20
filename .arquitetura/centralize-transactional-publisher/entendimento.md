# Entendimento da Tarefa: Centralizar ITransactionalPublisher no SharedKernel

## 📋 Contexto

Os serviços `CashFlow.Transactions.API` e `CashFlow.Consolidation.Worker` possuem implementações locais duplicadas de `ITransactionalPublisher` com o mesmo nome (`MassTransitPublisher`), violando o princípio DRY. Ambas implementam a mesma interface já definida no `CashFlow.SharedKernel`.

## 🎯 Objetivo

Centralizar as duas implementações em **um único componente** (`TransactionalPublisher`) no `CashFlow.SharedKernel/Infrastructure/Messaging/`, eliminando a duplicação e alinhando com o padrão do projeto de referência `order-taking`.

---

## 🔍 Análise Técnica

### Diferença entre as implementações atuais

| Método | Consolidation Worker | Transactions API |
|---|---|---|
| `Session` | Joga `InvalidOperationException` | Gerenciado via `IClientSessionHandle` |
| `BeginTransactionAsync` | No-op | Abre sessão + inicia transação |
| `CommitTransactionAsync` | No-op | Commita a transação |
| `PublishAsync` | Delega ao `IPublishEndpoint` | Delega ao `IPublishEndpoint` |
| `IDisposable` | ❌ | ✅ |

### Por que um único componente é viável

O `IngestTransactionsBatchCommandHandler` (Consumer context) **nunca chama** `BeginTransactionAsync`, `Session` ou `CommitTransactionAsync`. O MassTransit Outbox gerencia a transação internamente. Portanto, os métodos de sessão existem na implementação mas simplesmente não são invocados no contexto Consumer.

| Método | Worker Handler | API Handler |
|---|---|---|
| `BeginTransactionAsync` | ❌ Não chama | ✅ Chama |
| `Session` | ❌ Não acessa | ✅ Usa |
| `CommitTransactionAsync` | ❌ Não chama | ✅ Chama |
| `PublishAsync` | ✅ Chama | ✅ Chama |

### Alinhamento com projeto de referência (order-taking)

O projeto de referência possui uma única implementação `TransactionalPublisher` em `Infra.IoC/Messaging/`. Seguimos o mesmo padrão de naming e responsabilidade única.

---

## ✅ Escopo

### O que SERÁ feito
- Criar `TransactionalPublisher.cs` em `SharedKernel/Infrastructure/Messaging/`
- Atualizar DI do `Consolidation.Worker` para referenciar `TransactionalPublisher`
- Atualizar DI do `Transactions.API` para referenciar `TransactionalPublisher`
- Remover `Consolidation.Worker/Infrastructure/Messaging/MassTransitPublisher.cs`
- Remover `Transactions.API/Infrastructure/Messaging/MassTransitPublisher.cs`
- Remover overload `PublishAsync(routingKey)` existente no Worker (não está na interface, ignora parâmetro — código morto)
- Validar com `dotnet restore + build + test`

### O que NÃO será feito
- Alterar a interface `ITransactionalPublisher`
- Alterar os handlers (`CreateTransactionCommandHandler`, `IngestTransactionsBatchCommandHandler`)
- Alterar lógica de negócio

---

## 📁 Arquivos Afetados

### Criados
```
src/CashFlow.SharedKernel/Infrastructure/Messaging/
└── TransactionalPublisher.cs
```

### Modificados
```
src/consolidation/CashFlow.Consolidation.Worker/Extensions/ServiceCollectionExtensions.cs
  → using: CashFlow.Consolidation.Worker.Infrastructure.Messaging  → CashFlow.SharedKernel.Infrastructure.Messaging
  → tipo registrado: MassTransitPublisher → TransactionalPublisher

src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs
  → using: CashFlow.Transactions.API.Infrastructure.Messaging  → CashFlow.SharedKernel.Infrastructure.Messaging
  → tipo registrado: MassTransitPublisher → TransactionalPublisher
```

### Removidos
```
src/consolidation/CashFlow.Consolidation.Worker/Infrastructure/Messaging/MassTransitPublisher.cs
src/transactions/CashFlow.Transactions.API/Infrastructure/Messaging/MassTransitPublisher.cs
```

---

## 🔧 Design do TransactionalPublisher Centralizado

```csharp
namespace CashFlow.SharedKernel.Infrastructure.Messaging;

// Injeta: IMongoClient + IPublishEndpoint
// Session     → gerenciado internamente (lança se BeginTransactionAsync não foi chamado)
// BeginTransactionAsync → abre sessão + inicia transação (HTTP context)
// CommitTransactionAsync → commita (HTTP context)
// PublishAsync → _publishEndpoint.Publish (ambos os contextos)
// IDisposable  → descarta sessão ao fim do scope
```

**Nenhuma nova dependência no SharedKernel** — já possui `MongoDB.Driver` e `MassTransit.Abstractions`.

---

## 🚨 Riscos e Considerações

| Risco | Impacto | Mitigação |
|---|---|---|
| Quebra no DI container | Alto | `dotnet build` validará a resolução de tipos |
| Comportamento alterado | Baixo | Lógica copiada do `Transactions.API.MassTransitPublisher` sem alterações |
| `IPublishEndpoint` scoped no Worker | Baixo | MassTransit registra `IPublishEndpoint` como Scoped em ambos os serviços |
