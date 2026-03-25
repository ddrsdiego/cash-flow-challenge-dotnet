using System;
using MongoDB.Bson.Serialization.Attributes;

namespace CashFlow.SharedKernel.Domain.Entities;

/// <summary>
/// Distributed lock for coordinating single-instance operations across multiple Worker instances.
/// Used to ensure only one instance performs batch polling and dispatching.
/// Collection: distributed_locks
/// TTL index on ExpiresAt: automatic cleanup of expired locks
/// </summary>
public class DistributedLock
{
    /// <summary>
    /// Lock identifier (e.g., "transaction-batcher").
    /// Serves as MongoDB _id for fast O(1) lookups.
    /// </summary>
    [BsonId]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Instance ID holding this lock (e.g., pod name or hostname).
    /// </summary>
    [BsonElement("heldBy")]
    public string HeldBy { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the lock was acquired.
    /// </summary>
    [BsonElement("acquiredAt")]
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when this lock expires.
    /// TTL index ensures automatic cleanup if holder dies without renewal.
    /// Default TTL: 30 seconds (allows new holder to acquire within ~35 seconds).
    /// </summary>
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddSeconds(30);
}
