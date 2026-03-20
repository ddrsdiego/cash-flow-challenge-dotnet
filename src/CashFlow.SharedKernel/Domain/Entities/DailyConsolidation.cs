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
public class DailyConsolidation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// The date of the consolidation. Unique — one document per day.
    /// Stored as DateTime (date-only, time component set to 00:00:00 UTC).
    /// </summary>
    [BsonElement("date")]
    public DateTime Date { get; set; }

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
}
