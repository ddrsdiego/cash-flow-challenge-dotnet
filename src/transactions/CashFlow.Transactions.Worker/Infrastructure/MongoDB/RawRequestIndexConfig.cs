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
/// Marker interface for RawRequest index configuration.
/// Used for dependency injection and startup initialization.
/// </summary>
public interface IRawRequestIndexConfig : IMongoIndexConfigurator;

/// <summary>
/// Configures MongoDB indexes for RawRequest collection.
/// Collection: raw_requests
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class RawRequestIndexConfig :
    IRawRequestIndexConfig
{
    private readonly IMongoCollection<RawRequest> _collection;
    private readonly ILogger<RawRequestIndexConfig> _logger;

    public RawRequestIndexConfig(
        ITransactionsWorkerMongoDbContext context,
        ILogger<RawRequestIndexConfig> logger)
    {
        _collection = context.RawRequests ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EnsureIndexesAsync() => CreateIndexes();

    private async Task CreateIndexes()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<RawRequest>>
            {
                // Unique index on idempotencyKey
                new(Builders<RawRequest>.IndexKeys.Ascending(r => r.IdempotencyKey),
                    new CreateIndexOptions { Unique = true, Name = "idx_idempotencyKey_unique" }),

                // Compound index: status + createdAt (for FindPendingAsync)
                new(Builders<RawRequest>.IndexKeys
                    .Ascending(r => r.Status)
                    .Ascending(r => r.CreatedAt),
                    new CreateIndexOptions { Name = "idx_status_createdAt" }),

                // Compound index: status + dispatchedAt (for FindOrphanedDispatchedAsync sweep)
                new(Builders<RawRequest>.IndexKeys
                    .Ascending(r => r.Status)
                    .Ascending(r => r.DispatchedAt),
                    new CreateIndexOptions { Name = "idx_status_dispatchedAt" }),

                // TTL index on processedAt: expires documents after 30 days
                new(Builders<RawRequest>.IndexKeys.Ascending(r => r.ProcessedAt),
                    new CreateIndexOptions
                    {
                        ExpireAfter = TimeSpan.FromDays(30),
                        Name = "idx_processedAt_ttl_30days"
                    })
            };

            await _collection.Indexes.CreateManyAsync(indexModels);
            _logger.LogInformation("RawRequest indexes created");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for RawRequest collection");
        }
    }
}
