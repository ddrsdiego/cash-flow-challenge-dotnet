using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Infrastructure.MongoIndex;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

/// <summary>
/// Marker interface for ReceivedTransaction index configuration.
/// Used for dependency injection and startup initialization.
/// </summary>
public interface IReceivedTransactionIndexConfig : IMongoIndexConfigurator;

/// <summary>
/// Configures MongoDB indexes for ReceivedTransaction collection.
/// Collection: received_transactions
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class ReceivedTransactionIndexConfig : IReceivedTransactionIndexConfig
{
    private readonly IMongoCollection<ReceivedTransaction> _collection;
    private readonly ILogger<ReceivedTransactionIndexConfig> _logger;

    public ReceivedTransactionIndexConfig(
        ICashFlowMongoDbContext context,
        ILogger<ReceivedTransactionIndexConfig> logger)
    {
        _collection = context.ReceivedTransactions ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EnsureIndexesAsync() => CreateIndexes();

    private async Task CreateIndexes()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<ReceivedTransaction>>
            {
                // Index on BatchId for fast lookup by batch
                new(Builders<ReceivedTransaction>.IndexKeys.Ascending(t => t.BatchId),
                    new CreateIndexOptions { Name = "idx_batchId" }),

                // TTL index on ReceivedAt: expires documents after 30 days
                // Automatic cleanup without explicit deletion
                new(Builders<ReceivedTransaction>.IndexKeys.Ascending(t => t.ReceivedAt),
                    new CreateIndexOptions
                    {
                        ExpireAfter = TimeSpan.FromDays(30),
                        Name = "idx_receivedAt_ttl_30days"
                    })
            };

            await _collection.Indexes.CreateManyAsync(indexModels);
            _logger.LogInformation("ReceivedTransaction indexes created");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for ReceivedTransaction collection");
        }
    }
}
