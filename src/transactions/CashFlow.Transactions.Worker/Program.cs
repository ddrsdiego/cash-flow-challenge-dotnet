using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
using CashFlow.Transactions.Worker.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateDefaultBuilder(args);

builder.AddSerilogWithOpenTelemetry();

builder.ConfigureServices((context, services) =>
{
    services.AddMongoDb(context.Configuration);
    services.AddMassTransitWithRabbitMq(context.Configuration);
    services.AddMediatRHandlers();
    services.AddOpenTelemetryInstrumentation(context.Configuration);
    services.AddBatcherBackgroundService(context.Configuration);
});

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("CashFlow.Transactions.Worker starting...");

await host.InitializeIndexesAsync();
host.Run();
