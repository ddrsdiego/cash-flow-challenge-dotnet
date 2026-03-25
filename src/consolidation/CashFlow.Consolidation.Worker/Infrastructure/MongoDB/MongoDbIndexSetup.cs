using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

/// <summary>
/// Sets up MongoDB indexes for optimal query performance.
/// </summary>
public static class MongoDbIndexSetup
{
    /// <summary>
    /// Ensures all required indexes exist on MongoDB collections.
    /// Called once at application startup.
    /// </summary>
    public static async Task EnsureIndexesAsync(CashFlowMongoDbContext context, ILogger logger)
    {
        // DailyConsolidation indexes
        await CreateDailyConsolidationIndexes(context, logger);

        // ReceivedTransaction indexes
        await CreateReceivedTransactionIndexes(context, logger);
    }

    private static async Task CreateDailyConsolidationIndexes(CashFlowMongoDbContext context, ILogger logger)
    {
        try
        {
            var dailyConsolidationsCollection = context.DailyBalances;

            // Composite index: userId + date (for backward compatibility and validation)
            var userIdDateIndex = new CreateIndexModel<DailyBalances>(
                Builders<DailyBalances>.IndexKeys
                    .Ascending(c => c.UserId)
                    .Ascending(c => c.Date),
                new CreateIndexOptions { Unique = true, Name = "idx_userId_date_unique" });

            await dailyConsolidationsCollection.Indexes.CreateOneAsync(userIdDateIndex);

            // Unique index on consolidationKey for O(1) lookups
            // Format: {UserId}-{Date:yyyyMMdd} (e.g., "user123-20240115")
            var consolidationKeyIndex = new CreateIndexModel<DailyBalances>(
                Builders<DailyBalances>.IndexKeys.Ascending(c => c.ConsolidationKey),
                new CreateIndexOptions { Unique = true, Name = "idx_consolidationKey_unique" });

            await dailyConsolidationsCollection.Indexes.CreateOneAsync(consolidationKeyIndex);
        }
        catch (Exception e)
        {
            logger.LogError(e,"An error occured");
        }
    }

    private static async Task CreateReceivedTransactionIndexes(CashFlowMongoDbContext context, ILogger logger)
    {
        try
        {
            var receivedTransactionsCollection = context.ReceivedTransactions;

            // Index on BatchId for fast lookup by batch
            var batchIdIndex = new CreateIndexModel<ReceivedTransaction>(
                Builders<ReceivedTransaction>.IndexKeys.Ascending(t => t.BatchId),
                new CreateIndexOptions { Name = "idx_batchId" });

            await receivedTransactionsCollection.Indexes.CreateOneAsync(batchIdIndex);

            // TTL index on ReceivedAt: expires documents after 30 days
            // Automatic cleanup without explicit deletion
            var ttlIndex = new CreateIndexModel<ReceivedTransaction>(
                Builders<ReceivedTransaction>.IndexKeys.Ascending(t => t.ReceivedAt),
                new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.FromDays(30),
                    Name = "idx_receivedAt_ttl_30days"
                });

            await receivedTransactionsCollection.Indexes.CreateOneAsync(ttlIndex);
        }
        catch (Exception e)
        {
            logger.LogError(e,"An error occured");
        }
    }
}