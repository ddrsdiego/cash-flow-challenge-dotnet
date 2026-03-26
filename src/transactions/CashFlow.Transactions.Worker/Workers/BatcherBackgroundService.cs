using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;
using MediatR;

using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Workers;

/// <summary>
/// Background service that polls for pending raw requests and dispatches them as batches.
/// Only one instance across all Worker instances holds the distributed lock at any time.
/// Runs every DelayOnEmptyMs milliseconds.
/// Uses IServiceScopeFactory to resolve scoped dependencies (repositories, handlers).
/// </summary>
public sealed class BatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BatcherBackgroundService> _logger;

    private string _instanceId = string.Empty;
    private int _batchSize = 100;
    private int _delayOnEmptyMs = 5000;
    private int _lockTtlSeconds = 30;
    private int _sweepThresholdMinutes = 5;

    public BatcherBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<BatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        _batchSize = _configuration.GetValue("Batcher:BatchSize", 100);
        _delayOnEmptyMs = _configuration.GetValue("Batcher:DelayOnEmptyMs", 1_000);
        _delayOnEmptyMs = _configuration.GetValue("Batcher:DelayOnEmptyMs", 5000);
        _lockTtlSeconds = _configuration.GetValue("Batcher:LockTtlSeconds", 30);
        _sweepThresholdMinutes = _configuration.GetValue("Batcher:SweepThresholdMinutes", 5);

        _logger.LogInformation("BatcherBackgroundService starting... InstanceId: {InstanceId}", _instanceId);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BatcherBackgroundService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Create a new scope for each cycle to resolve scoped dependencies
                await using var scope = _scopeFactory.CreateAsyncScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var tracerId = Guid.NewGuid().ToString("N");
                var command = new DispatchTransactionBatchCommand(
                    TracerId: tracerId,
                    InstanceId: _instanceId,
                    BatchSize: _batchSize,
                    LockTtlSeconds: _lockTtlSeconds,
                    SweepThresholdMinutes: _sweepThresholdMinutes);

                var response = await mediator.Send(command, stoppingToken);
                if (response.IsFailure)
                    _logger.LogWarning("Failed to process batch cycle: {@Response}", response);

                if (response is { IsSuccess: true, StatusCode: StatusCodes.Status202Accepted })
                    continue;

                await Task.Delay(_delayOnEmptyMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Batcher polling loop");
            }
        }

        _logger.LogInformation("BatcherBackgroundService stopped");
    }
}
