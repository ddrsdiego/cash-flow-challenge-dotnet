using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CashFlow.SharedKernel.Domain.Entities;

/// <summary>
/// Represents the daily cash flow consolidation for a specific date.
/// One document per date (enforced by unique index on date field).
/// Collection: daily_consolidation
/// </summary>
[BsonIgnoreExtraElements]
public class DailyBalances
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// The user ID (merchant) for this consolidation.
    /// Scopes the consolidation to a specific merchant.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The date of the consolidation. Unique per merchant per day (composite key: userId + date).
    /// Stored as DateTime (date-only, time component set to 00:00:00 UTC).
    /// </summary>
    [BsonElement("date")]
    public DateTime Date { get; set; }

    /// <summary>
    /// Composite key: {UserId-sem-hifens}_{Date:yyyyMMdd} (e.g., "f0a00b110b2f4495913df00df3b12a65_20260320").
    /// Used for O(1) lookups via unique index on this field.
    /// Should be set via ConsolidationKey.Value when creating/updating.
    /// </summary>
    [BsonElement("consolidationKey")]
    public string ConsolidationKey { get; set; } = string.Empty;

    /// <summary>
    /// Sum of all credit transactions for the day.
    /// </summary>
    [BsonElement("totalCredits")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalCredits { get; set; }

    /// <summary>
    /// Sum of all debit transactions for the day.
    /// </summary>
    [BsonElement("totalDebits")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal TotalDebits { get; set; }

    /// <summary>
    /// Net balance for the day. Invariant: Balance = TotalCredits - TotalDebits.
    /// </summary>
    [BsonElement("balance")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Balance { get; set; }

    /// <summary>
    /// Total number of transactions processed for the day.
    /// </summary>
    [BsonElement("transactionCount")]
    public int TransactionCount { get; set; }

    /// <summary>
    /// Timestamp of the last recalculation by the Consolidation Worker.
    /// </summary>
    [BsonElement("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Applies a credit delta to the consolidation. Recalculates balance.
    /// </summary>
    public void ApplyCredit(decimal amount)
    {
        TotalCredits += amount;
        Balance = TotalCredits - TotalDebits;
        TransactionCount++;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Applies a debit delta to the consolidation. Recalculates balance.
    /// </summary>
    public void ApplyDebit(decimal amount)
    {
        TotalDebits += amount;
        Balance = TotalCredits - TotalDebits;
        TransactionCount++;
        LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// Applies pre-aggregated credit and debit totals in a single operation.
    /// More efficient than calling ApplyCredit/ApplyDebit multiple times in a loop.
    /// Useful for batch processing where totals are pre-computed by LINQ aggregation.
    /// </summary>
    /// <param name="totalCredits">Pre-aggregated sum of all credit transactions</param>
    /// <param name="totalDebits">Pre-aggregated sum of all debit transactions</param>
    /// <param name="count">Total number of transactions being applied</param>
    public void ApplyBatch(decimal totalCredits, decimal totalDebits, int count)
    {
        TotalCredits += totalCredits;
        TotalDebits += totalDebits;
        Balance = TotalCredits - TotalDebits;
        TransactionCount += count;
        LastUpdated = DateTime.UtcNow;
    }
}
