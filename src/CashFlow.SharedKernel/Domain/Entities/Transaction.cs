using System;
using CashFlow.SharedKernel.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CashFlow.SharedKernel.Domain.Entities;

/// <summary>
/// Represents a financial transaction (debit or credit) in the cash flow system.
/// Collection: transactions
/// </summary>
[BsonIgnoreExtraElements]
public class Transaction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("type")]
    [BsonRepresentation(BsonType.String)]
    public TransactionType Type { get; set; }

    [BsonElement("amount")]
    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Amount { get; set; }

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("category")]
    [BsonRepresentation(BsonType.String)]
    public Category Category { get; set; }

    [BsonElement("date")]
    public DateTime Date { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
