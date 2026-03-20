# Análise de Diagnóstico — Mensagens RabbitMQ não são consumidas

**Status:** Em progresso  
**Data de Criação:** 2026-03-20  
**Responsável:** Cline (Análise Automática)

---

## 📋 Contexto

As mensagens `TransactionCreatedEvent` publicadas pelo componente `CashFlow.Transactions.API` **não estão sendo consumidas** pelo componente `CashFlow.Consolidation.Worker` via RabbitMQ.

### Sintomas Observados
- ✅ Transactions.API publica transações sem erros
- ❌ Consolidation.Worker Consumer nunca recebe as mensagens
- ❌ Fila RabbitMQ nunca é criada/populada

---

## 🔍 Problemas Identificados

### 🔴 Problema #1 — CRÍTICO: Transactions.API não conecta ao RabbitMQ

**Localização:** `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs`

**Código atual (❌ BUG):**
```csharp
var host = configuration["RabbitMQ:Host"];
```

**Problema:**
- O `docker-compose.yml` passa: `RabbitMQ__HostName: rabbitmq` (mapeado para `"RabbitMQ:HostName"`)
- O código procura por: `"RabbitMQ:Host"`
- Em .NET IConfiguration, essas são **chaves diferentes**
- **Resultado:** `host = null` → MassTransit tenta conectar em `localhost:5672` dentro do container → falha silenciosa

**Como foi descoberto:**
- O Worker (Consolidation) já implementa corretamente: `var host = configuration["RabbitMQ:HostName"] ?? configuration["RabbitMQ:Host"];`
- Comparação lado-a-lado revelou a inconsistência

**Impacto:** 
- Nenhuma mensagem é publicada para o RabbitMQ
- Consumer nunca tem oportunidade de receber

---

### 🔴 Problema #2 — CRÍTICO: Exchange name mismatch entre Publisher e Consumer

**Localização (Publisher):** `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs`
**Localização (Consumer):** `src/consolidation/CashFlow.Consolidation.Worker/Consumers/TransactionCreatedConsumerDefinition.cs`

**Situação atual:**

| Lado | Exchange criado | Configuração |
|------|---|---|
| **Publisher** | `transaction-created` | `cfg.Message<TransactionCreatedEvent>(m => m.SetEntityName("transaction-created"))` |
| **Consumer** | `CashFlow.SharedKernel.Messages:TransactionCreatedEvent` (auto-gerado) | Nenhuma configuração (`SetEntityName`) |

**Problema:**
- O atributo `[MessageUrn("transaction-created")]` na classe `TransactionCreatedEvent` afeta **apenas a serialização JSON** (`messageType` header), **não** o nome do exchange RabbitMQ
- Cada lado cria seu próprio exchange automaticamente via MassTransit
- **Resultado:** Publisher publica em exchange A, Consumer escuta no exchange B → mensagens não chegam

**Arquitectura esperada:**
- Ambos devem usar o **mesmo exchange**: `cashflow.transactions` (topic exchange com routing key `transaction.created`)
- Isso está definido em `definitions.json` e é a intenção original da topologia RabbitMQ

**Impacto:**
- Consumer vincula a fila `consolidation.process` ao exchange errado
- Binding é criado em exchange inexistente (auto-gerado pelo Consumer)
- Publisher publica em outro exchange (auto-gerado pelo Publisher)

---

### 🔴 Problema #3 — CRÍTICO: Binding aponta para exchange que não recebe mensagens

**Localização:** `src/consolidation/CashFlow.Consolidation.Worker/Consumers/TransactionCreatedConsumerDefinition.cs`

**Código:**
```csharp
rmq.Bind(RabbitMqEndpointNames.TransactionCreated.Exchange, x =>
{
    x.ExchangeType = "topic";
    x.RoutingKey = "transaction.created";  // ← "cashflow.transactions"
});
```

**Problema:**
- O `ConsumerDefinition` tenta binding no exchange `cashflow.transactions` (intenção correta)
- **Mas** o Publisher **não publica para lá** — publica para `transaction-created` (fanout)
- Além disso, este exchange `cashflow.transactions` é definido em `definitions.json`, **que nunca é carregado no RabbitMQ** (sem volume mount, sem env var)

**Impacto:**
- Binding é código morto
- Fila `consolidation.process` fica vazia

---

### 🟡 Problema #4 — Secundário: definitions.json nunca é importado

**Localização:** `docker-compose.yml`, serviço `rabbitmq`

**Problema:**
```yaml
rabbitmq:
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
    # ← Sem RABBITMQ_LOAD_DEFINITIONS
    # ← Sem volume mount de definitions.json
```

- RabbitMQ Management API permite carregar topologia via arquivo JSON
- Este projeto tem `definitions.json` na raiz com topologia completa
- **Mas nunca é carregado** → exchanges/queues/bindings ficam indefinidos

**Impacto:**
- `definitions.json` fica como documentação sem efeito
- Topologia é criada dinamicamente e fragmentada (cada lado cria o seu)

---

### 🟡 Problema #5 — Secundário: VirtualHost inconsistente

**Localização:** `definitions.json` vs `appsettings.json` vs `docker-compose.yml`

| Onde | VirtualHost |
|---|---|
| `definitions.json` | `"cashflow"` |
| `appsettings.json` (Transactions.API) | `/` *(não configurado)* |
| `appsettings.json` (Consolidation.Worker) | `/` *(não configurado)* |
| `docker-compose.yml` | `/` *(padrão, não sobrescrito)* |

**Problema:**
- Topologias em vhosts diferentes não interagem
- `definitions.json` teria de ser importado no vhost `cashflow`, mas serviços usam `/`

**Impacto:**
- `definitions.json` é incoerente com comportamento real

---

## ✅ Solução Proposta

### Estratégia: Alinhar tudo com `cashflow.transactions` (topic exchange)

Esta é a arquitetura **intencionada** — usar topic exchange para suportar múltiplos routing keys no futuro.

---

### 📝 Modificações Necessárias

#### 1️⃣ Criar documento de rastreabilidade
**Arquivo:** `.arquitetura/fix-rabbitmq-message-routing/entendimento.md`  
**Ação:** ✅ Criado (este arquivo)

#### 2️⃣ Estender ITransactionalPublisher com routing key
**Arquivo:** `src/CashFlow.SharedKernel/Interfaces/ITransactionalPublisher.cs`

```csharp
// Novo overload para suportar routing key (usado no Publisher)
Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class;
```

#### 3️⃣ Implementar routing key no Publisher (Transactions.API)
**Arquivo:** `src/transactions/CashFlow.Transactions.API/Infrastructure/Messaging/MassTransitPublisher.cs`

```csharp
public Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class =>
    _publishEndpoint.Publish(message, ctx => ctx.SetRoutingKey(routingKey), cancellationToken);
```

#### 4️⃣ Implementar routing key no Publisher (Consolidation Worker)
**Arquivo:** `src/consolidation/CashFlow.Consolidation.Worker/Infrastructure/Messaging/MassTransitPublisher.cs`

```csharp
// No-op — no Consumer, o MassTransit gerencia o routing automaticamente
public Task PublishAsync<T>(T message, string routingKey, CancellationToken cancellationToken = default) where T : class =>
    PublishAsync(message, cancellationToken); // Ignora routing key
```

#### 5️⃣ Corrigir Transactions.API — Bug #1 (host config)
**Arquivo:** `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs`

```csharp
// ❌ Antes
var host = configuration["RabbitMQ:Host"];

// ✅ Depois
var host = configuration["RabbitMQ:HostName"] ?? configuration["RabbitMQ:Host"];
```

#### 6️⃣ Corrigir Transactions.API — Bug #2 (exchange name)
**Arquivo:** `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs`

```csharp
// ❌ Antes
cfg.Message<TransactionCreatedEvent>(m =>
    m.SetEntityName("transaction-created"));

// ✅ Depois
cfg.Message<TransactionCreatedEvent>(m =>
    m.SetEntityName("cashflow.transactions"));

cfg.Publish<TransactionCreatedEvent>(p =>
    p.ExchangeType = "topic");
```

#### 7️⃣ Corrigir CreateTransactionCommandHandler
**Arquivo:** `src/transactions/CashFlow.Transactions.API/Application/UseCases/CreateTransaction/CreateTransactionCommandHandler.cs`

```csharp
// ❌ Antes
await _transactionalPublisher.PublishAsync(transactionCreatedEvent, cancellationToken);

// ✅ Depois
await _transactionalPublisher.PublishAsync(transactionCreatedEvent, "transaction.created", cancellationToken);
```

#### 8️⃣ Corrigir Consolidation.Worker — Bug #2 (exchange name no Consumer)
**Arquivo:** `src/consolidation/CashFlow.Consolidation.Worker/Extensions/ServiceCollectionExtensions.cs`

```csharp
// Adicionar após x.AddConsumers(...)
x.Message<TransactionCreatedEvent>(m =>
    m.SetEntityName("cashflow.transactions"));

x.Publish<TransactionCreatedEvent>(p =>
    p.ExchangeType = "topic");
```

#### 9️⃣ Corrigir definitions.json — VirtualHost
**Arquivo:** `definitions.json`

```json
// ❌ Antes
{ "name": "cashflow" }

// ✅ Depois
{ "name": "/" }
```

#### 🔟 Configurar RabbitMQ para carregar definitions.json
**Arquivo:** `docker-compose.yml`

```yaml
rabbitmq:
    environment:
      RABBITMQ_LOAD_DEFINITIONS: "true"
      RABBITMQ_DEFINITIONS_FILE: "/etc/rabbitmq/definitions.json"
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
      - ./definitions.json:/etc/rabbitmq/definitions.json:ro
```

---

## 🗺️ Fluxo Após a Correção

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      FLUXO CORRETO (APÓS FIXES)                            │
└─────────────────────────────────────────────────────────────────────────────┘

Transactions.API                                RabbitMQ                          Consolidation.Worker
─────────────────                               ────────────                      ────────────────────

1. CreateTransaction command received

2. PersistTransactionAsync() called

3. _transactionalPublisher.PublishAsync(
      transactionCreatedEvent,
      "transaction.created"          ─────────────►  Exchange: cashflow.transactions
    )                                             (topic, durable)
                                                     │
                                                     │ routingKey: "transaction.created"
                                                     │
                                                     ▼
                                                 Queue: consolidation.process
                                                     │
                                                     │
                                                     ▼
                                        4. TransactionCreatedConsumer
                                           .Consume(ConsumeContext)
                                           
                                           5. _mediator.Send(
                                                IngestTransactionsBatchCommand
                                              )
```

---

## 📊 Resumo das Mudanças

| # | Arquivo | Tipo | Impacto | Criticidade |
|---|---|---|---|---|
| 1 | `.arquitetura/fix-rabbitmq-message-routing/entendimento.md` | Criar | Documentação | Info |
| 2 | `ITransactionalPublisher.cs` | Modificar | Adiciona overload | Médio |
| 3 | `Transactions.API/MassTransitPublisher.cs` | Modificar | Implementa overload | Médio |
| 4 | `Consolidation.Worker/MassTransitPublisher.cs` | Modificar | Implementa overload | Médio |
| 5 | `Transactions.API/ServiceCollectionExtensions.cs` | Modificar (2x) | Fixa bugs #1 e #2 | CRÍTICO |
| 6 | `CreateTransactionCommandHandler.cs` | Modificar | Usa novo overload | CRÍTICO |
| 7 | `Consolidation.Worker/ServiceCollectionExtensions.cs` | Modificar | Alinha exchange | CRÍTICO |
| 8 | `definitions.json` | Modificar | Alinha vhost | Médio |
| 9 | `docker-compose.yml` | Modificar | Carrega definitions.json | CRÍTICO |

---

## ✔️ Validação Após Implementação

- [ ] `dotnet restore` — resolve dependências
- [ ] `dotnet build` — compila sem erros
- [ ] `docker-compose up -d` — infra sobe corretamente
- [ ] `docker logs cashflow-rabbitmq | grep definitions` — verifica carregamento
- [ ] RabbitMQ Management (http://localhost:15672) — inspeciona exchanges/queues/bindings
- [ ] Criar transação via API → verificar se Consumer processa
- [ ] `dotnet test` — testes passam

---

## 📚 Referências

- [MassTransit RabbitMQ Documentation](https://masstransit.io/documentation/configuration/transports/rabbitmq)
- [RabbitMQ Definitions File Format](https://www.rabbitmq.com/docs/management-cli)
- [Topic Exchange Routing](https://www.rabbitmq.com/tutorials/amqp-concepts.html)


