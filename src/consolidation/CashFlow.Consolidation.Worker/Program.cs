using CashFlow.SharedKernel.Infrastructure.Extensions;
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
using CashFlow.Consolidation.Worker.Extensions;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args);

builder.AddSerilogWithOpenTelemetry();

builder.ConfigureServices((context, services) =>
{
    services.AddMongoDb(context.Configuration);
    services.AddMassTransitWithRabbitMq(context.Configuration);
    services.AddMediatRHandlers();
    services.AddOpenTelemetryInstrumentation(context.Configuration);
});

var host = builder.Build();

await host.InitializeIndexesAsync();

host.Run();
