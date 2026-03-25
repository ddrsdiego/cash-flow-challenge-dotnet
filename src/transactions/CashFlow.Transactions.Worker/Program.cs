using CashFlow.SharedKernel.Infrastructure.Extensions;
<<<<<<< HEAD
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
=======
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
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

<<<<<<< HEAD
await host.InitializeIndexesAsync();

=======
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
host.Run();
