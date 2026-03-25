using System;
using CashFlow.SharedKernel.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CashFlow.SharedKernel.Domain.Entities;

/// <summary>
/// Represents a transaction received in a batch from the Transactions API.
/// Persisted in MongoDB for fast ingestion without transformation.
/// Used by the Consolidation Worker for batch processing.
/// </summary>
public sealed class ReceivedTransaction
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Unique identifier of the transaction (from source system).
    /// </summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Batch ID for grouping transactions received together.
    /// </summary>
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// User ID who owns this transaction.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Type of transaction (Credit or Debit).
    /// </summary>
    public TransactionType Type { get; set; }

    /// <summary>
    /// Amount of the transaction.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Category of the transaction.
    /// </summary>
    public Category Category { get; set; }

    /// <summary>
    /// Date of the transaction.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Timestamp when the transaction was received and persisted.
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    public string TracerId { get; set; } = string.Empty;
}
