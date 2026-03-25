using System;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Sets up MongoDB indexes for optimal query performance.
/// Called once at application startup.
/// </summary>
public static class MongoDbIndexSetup
{
    /// <summary>
    /// Ensures all required indexes exist on MongoDB collections.
    /// Idempotent: safe to call multiple times.
    /// </summary>
    public static async Task EnsureIndexesAsync(ITransactionsWorkerMongoDbContext context, ILogger logger)
    {
        try
        {
            await CreateRawRequestIndexes(context, logger);
            await CreateDistributedLockIndexes(context, logger);
            logger.LogInformation("MongoDB indexes created successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create MongoDB indexes");
            throw;
        }
    }

    private static async Task CreateRawRequestIndexes(ITransactionsWorkerMongoDbContext context, ILogger logger)
    {
        var collection = context.RawRequests;

        // Unique index on idempotencyKey
        var idempotencyKeyIndex = new CreateIndexModel<RawRequest>(
            Builders<RawRequest>.IndexKeys.Ascending(r => r.IdempotencyKey),
            new CreateIndexOptions { Unique = true, Name = "idx_idempotencyKey_unique" });

        // Compound index: status + createdAt (for FindPendingAsync)
        var statusCreatedAtIndex = new CreateIndexModel<RawRequest>(
            Builders<RawRequest>.IndexKeys
                .Ascending(r => r.Status)
                .Ascending(r => r.CreatedAt),
            new CreateIndexOptions { Name = "idx_status_createdAt" });

        // Compound index: status + dispatchedAt (for FindOrphanedDispatchedAsync sweep)
        var statusDispatchedAtIndex = new CreateIndexModel<RawRequest>(
            Builders<RawRequest>.IndexKeys
                .Ascending(r => r.Status)
                .Ascending(r => r.DispatchedAt),
            new CreateIndexOptions { Name = "idx_status_dispatchedAt" });

        // TTL index on processedAt: expires documents after 30 days
        var ttlIndex = new CreateIndexModel<RawRequest>(
            Builders<RawRequest>.IndexKeys.Ascending(r => r.ProcessedAt),
            new CreateIndexOptions
            {
                ExpireAfter = TimeSpan.FromDays(30),
                Name = "idx_processedAt_ttl_30days"
            });

        await collection.Indexes.CreateOneAsync(idempotencyKeyIndex);
        await collection.Indexes.CreateOneAsync(statusCreatedAtIndex);
        await collection.Indexes.CreateOneAsync(statusDispatchedAtIndex);
        await collection.Indexes.CreateOneAsync(ttlIndex);

        logger.LogInformation("RawRequest indexes created");
    }

    private static async Task CreateDistributedLockIndexes(ITransactionsWorkerMongoDbContext context, ILogger logger)
    {
        var collection = context.DistributedLocks;

        // TTL index on expiresAt: automatic cleanup of expired locks
        var ttlIndex = new CreateIndexModel<DistributedLock>(
            Builders<DistributedLock>.IndexKeys.Ascending(l => l.ExpiresAt),
            new CreateIndexOptions
            {
                ExpireAfter = TimeSpan.Zero,  // Delete immediately when expiresAt is reached
                Name = "idx_expiresAt_ttl"
            });

        await collection.Indexes.CreateOneAsync(ttlIndex);

        logger.LogInformation("DistributedLock indexes created");
    }
}
