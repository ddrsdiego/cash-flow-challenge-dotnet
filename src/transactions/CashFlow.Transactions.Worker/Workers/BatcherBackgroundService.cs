using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;
using MediatR;
<<<<<<< HEAD
using Microsoft.AspNetCore.Http;
=======
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
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
<<<<<<< HEAD
        _delayOnEmptyMs = _configuration.GetValue("Batcher:DelayOnEmptyMs", 1_000);
=======
        _delayOnEmptyMs = _configuration.GetValue("Batcher:DelayOnEmptyMs", 5000);
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
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
<<<<<<< HEAD
=======
                // Create a new scope for each cycle to resolve scoped dependencies
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
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
<<<<<<< HEAD
                if (response.IsFailure)
                    _logger.LogWarning("Failed to process batch cycle: {@Response}", response);

                if (response is { IsSuccess: true, StatusCode: StatusCodes.Status202Accepted })
                    continue;

                await Task.Delay(_delayOnEmptyMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
=======

                // No-op responses (200 Ok, lock not acquired, no pending) are normal
                // Only log failures
                if (response.IsFailure)
                {
                    _logger.LogWarning("Failed to process batch cycle: {@Response}", response);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when host is stopping
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Batcher polling loop");
            }
<<<<<<< HEAD
=======

            // Delay before next cycle
            await Task.Delay(_delayOnEmptyMs, stoppingToken);
>>>>>>> 55c15ded73d5f33778101db5027f405e73103f12
        }

        _logger.LogInformation("BatcherBackgroundService stopped");
    }
}
