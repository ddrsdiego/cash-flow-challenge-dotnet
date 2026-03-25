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
/// Marker interface for DailyBalances index configuration.
/// Used for dependency injection and startup initialization.
/// </summary>
public interface IDailyBalancesIndexConfig : IMongoIndexConfigurator;

/// <summary>
/// Configures MongoDB indexes for DailyBalances collection.
/// Collection: daily_balances
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class DailyBalancesIndexConfig : IDailyBalancesIndexConfig
{
    private readonly IMongoCollection<DailyBalances> _collection;
    private readonly ILogger<DailyBalancesIndexConfig> _logger;

    public DailyBalancesIndexConfig(
        ICashFlowMongoDbContext context,
        ILogger<DailyBalancesIndexConfig> logger)
    {
        _collection = context.DailyBalances ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task EnsureIndexesAsync() => CreateIndexes();

    private async Task CreateIndexes()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<DailyBalances>>
            {
                // Composite index: userId + date (for backward compatibility and validation)
                new(Builders<DailyBalances>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Ascending(c => c.Date),
                    new CreateIndexOptions { Unique = true, Name = "idx_userId_date_unique" }),

                // Unique index on consolidationKey for O(1) lookups
                // Format: {UserId}-{Date:yyyyMMdd} (e.g., "user123-20240115")
                new(Builders<DailyBalances>.IndexKeys.Ascending(c => c.ConsolidationKey),
                    new CreateIndexOptions { Unique = true, Name = "idx_consolidationKey_unique" })
            };

            await _collection.Indexes.CreateManyAsync(indexModels);
            _logger.LogInformation("DailyBalances indexes created");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for DailyBalances collection");
        }
    }
}
