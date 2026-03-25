using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Marker interface for DistributedLock index configuration.
/// Used for dependency injection and startup initialization.
/// </summary>
public interface IDistributedLockIndexConfig : IMongoIndexConfigurator;

/// <summary>
/// Configures MongoDB indexes for DistributedLock collection.
/// Collection: distributed_locks
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class DistributedLockIndexConfig : IDistributedLockIndexConfig
{
    private readonly IMongoCollection<DistributedLock> _collection;
    private readonly ILogger<DistributedLockIndexConfig> _logger;

    public DistributedLockIndexConfig(
        ITransactionsWorkerMongoDbContext context,
        ILogger<DistributedLockIndexConfig> logger)
    {
        _collection = context.DistributedLocks ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EnsureIndexesAsync() => CreateIndexes();

    private async Task CreateIndexes()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<DistributedLock>>
            {
                // TTL index on expiresAt: automatic cleanup of expired locks
                new(Builders<DistributedLock>.IndexKeys.Ascending(l => l.ExpiresAt),
                    new CreateIndexOptions
                    {
                        ExpireAfter = TimeSpan.Zero,  // Delete immediately when expiresAt is reached
                        Name = "idx_expiresAt_ttl"
                    })
            };

            await _collection.Indexes.CreateManyAsync(indexModels);
            _logger.LogInformation("DistributedLock indexes created");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for DistributedLock collection");
        }
    }
}
