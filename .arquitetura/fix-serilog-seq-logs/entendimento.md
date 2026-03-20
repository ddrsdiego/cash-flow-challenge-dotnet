# Fix: Logs do CashFlow.Transactions.API não chegam ao SEQ

## 📋 Contexto

O sistema **CashFlow.Transactions.API** não está enviando logs para o **SEQ**.

O **OTel Collector** está corretamente configurado com pipeline de logs → SEQ (conforme `infra/config/otel/otel-collector-config.yml`), mas a aplicação não alimenta esse pipeline.

---

## 🔴 Causas Raiz Identificadas

### 1. **Pipeline de Logs Ausente no OpenTelemetry** (CRÍTICO)

**Arquivo:** `src/transactions/CashFlow.Transactions.API/Extensions/ServiceCollectionExtensions.cs`

O método `AddOpenTelemetryInstrumentation()` configura **apenas traces**:

```csharp
// ❌ ATUAL — só .WithTracing()
services.AddOpenTelemetry()
    .ConfigureResource(...)
    .WithTracing(tracing =>  // ← Apenas traces
    {
        // configuração de traces
    });
    // ← FALTA: .WithLogging(...)
```

**Resultado:** Nenhum log chega ao OTel Collector, portanto nenhum log chega ao SEQ.

---

### 2. **Env Var Mismatch no docker-compose** (CRÍTICO)

**Arquivo:** `docker-compose.yml` vs `ServiceCollectionExtensions.cs`

| Localização | Configuração | Status |
|---|---|---|
| `docker-compose.yml` | `OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector:4317"` | ❌ Não mapeia |
| `Program.cs / appsettings.json` | `configuration["OpenTelemetry:OtlpEndpoint"]` | ✅ Esperado |

**Por quê não mapeia?**
No .NET Configuration System, env vars com `_` são normalizadas para `:` (hierarquia), mas `OTEL_EXPORTER_OTLP_ENDPOINT` não mapeia para `OpenTelemetry:OtlpEndpoint`.

**Resultado em Docker:**
- `configuration["OpenTelemetry:OtlpEndpoint"]` lê o valor padrão: `"http://localhost:4317"` ❌
- Endpoint correto seria: `"http://otel-collector:4317"` ✅
- Afeta **TANTO traces quanto logs**

---

### 3. **Serilog Não Configurado**

O projeto usa apenas o provider de logging padrão do .NET (por interface `ILogger`). O design documentado no `otel-collector-config.yml` assume **Serilog** como logging provider, que oferece:

- ✅ Structured logging (JSON)
- ✅ Automatic trace correlation (lê `Activity` corrente)
- ✅ Easy integration com OpenTelemetry Sink
- ✅ Configuration via `appsettings.json`

---

## ✅ Solução Proposta

Seguindo o **padrão do projeto de referência** `cora-integration`:

### 1. **Adicionar Packages**
- `Serilog.AspNetCore` (host integration)
- `Serilog.Sinks.OpenTelemetry` (OTLP export)

### 2. **Configurar Serilog em Program.cs**
```csharp
builder.Host.UseSerilog((context, cfg) =>
{
    var otlpEndpoint = context.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";
    var serviceName = context.Configuration["OpenTelemetry:ServiceName"] ?? "cashflow-transactions-api";

    cfg.ReadFrom.Configuration(context.Configuration)  // Console sink vem de appsettings
       .WriteTo.OpenTelemetry(opts =>                  // OTel sink (OTLP gRPC)
       {
           opts.Endpoint = otlpEndpoint;
           opts.Protocol = OtlpProtocol.Grpc;
           opts.ResourceAttributes = new Dictionary<string, object>
           {
               ["service.name"] = serviceName
           };
       });
});
```

### 3. **Configurar Serilog em appsettings.json**
```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.Console"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "MassTransit": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "formatter": "Serilog.Formatting.Json.JsonFormatter, Serilog"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"],
    "Properties": {
      "Application": "cashflow-transactions-api"
    }
  }
}
```

### 4. **Corrigir Env Vars no docker-compose.yml**
```yaml
# ❌ ATUAL (não mapeia)
OTEL_EXPORTER_OTLP_ENDPOINT: "http://otel-collector:4317"
OTEL_SERVICE_NAME: cashflow-transactions-api

# ✅ CORRETO (__ como separador hierárquico)
OpenTelemetry__OtlpEndpoint: "http://otel-collector:4317"
OpenTelemetry__ServiceName: cashflow-transactions-api
```

---

## 🏗️ Fluxo de Logs Após Fix

```
CashFlow.Transactions.API
  │
  ├─► Serilog Console Sink (JSON) 
  │   └─► stdout / docker logs
  │
  └─► Serilog OTel Sink (OTLP gRPC)
      └─► OTel Collector :4317
          └─► logs pipeline (otlphttp/seq)
              └─► Seq :5341 ✅
```

---

## 📁 Arquivos Modificados

| Arquivo | Mudança | Status |
|---|---|---|
| `Directory.Packages.props` | +2 packages no grupo Observability | ✅ |
| `src/transactions/CashFlow.Transactions.API/CashFlow.Transactions.API.csproj` | +2 package references | ✅ |
| `src/transactions/CashFlow.Transactions.API/Program.cs` | `builder.Host.UseSerilog(...)` | ✅ |
| `src/transactions/CashFlow.Transactions.API/appsettings.json` | Substituir `Logging` por `Serilog` | ✅ |
| `docker-compose.yml` | Corrigir 2 env vars (OpenTelemetry__*) | ✅ |

---

## 🎯 Escopo

### ✅ IN SCOPE
- **CashFlow.Transactions.API**: Configurar Serilog + corrigir env vars + fix OTel logs

### ⚠️ OUT OF SCOPE (tarefa futura)
- **CashFlow.Consolidation.API**: Sem OTel algum (problema separado)
- **CashFlow.Consolidation.Worker**: A verificar

---

## 📚 Referência

- **Projeto de referência:** `C:\Users\User\Documents\desenvolvimento\dotnet\cora-integration\src\WebApi\`
  - `Program.cs`: Padrão Serilog simples
  - `appsettings.json`: Seção Serilog com Console sink JSON
  - `WebApi.csproj`: Apenas `Serilog.AspNetCore`

- **Design Intent:** `infra/config/otel/otel-collector-config.yml`
  ```yaml
  # App writes JSON to stdout (Serilog Console Sink) for `docker logs`
  # App sends logs via OpenTelemetry Logs SDK through same OTLP channel
  ```

---

**Status:** ✅ Pronto para implementação
**Data:** 2026-03-20
