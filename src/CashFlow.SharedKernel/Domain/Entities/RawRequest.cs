using System;
using System.Collections.Generic;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Messages;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CashFlow.SharedKernel.Domain.Entities;

/// <summary>
/// Represents a raw transaction request received from the API.
/// Persisted immediately in MongoDB for fast ingestion (202 Accepted response).
/// Processed asynchronously in batches by the Transactions Worker.
/// Collection: raw_requests
/// </summary>
public class RawRequest
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>
    /// Unique idempotency key provided by the client (request deduplication).
    /// Compound index: unique
    /// </summary>
    [BsonElement("idempotencyKey")]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// User ID who submitted this request.
    /// </summary>
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// List of transactions in this request.
    /// </summary>
    [BsonElement("transactions")]
    public List<RawTransactionItem> Transactions { get; set; } = new();

    /// <summary>
    /// Current status in the batch processing pipeline.
    /// </summary>
    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public RawRequestStatus Status { get; set; } = RawRequestStatus.Pending;

    /// <summary>
    /// Timestamp when the request was received.
    /// Compound index: status + createdAt
    /// </summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the request was dispatched to a batch.
    /// Compound index: status + dispatchedAt (for sweep of orphaned items)
    /// </summary>
    [BsonElement("dispatchedAt")]
    public DateTime DispatchedAt { get; set; }

    /// <summary>
    /// Timestamp when the request was successfully processed.
    /// TTL index: automatic cleanup after 30 days
    /// </summary>
    [BsonElement("processedAt")]
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    [BsonElement("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Batch ID when dispatched (groups multiple raw requests into a single batch).
    /// </summary>
    [BsonElement("batchId")]
    public string BatchId { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for distributed tracing.
    /// </summary>
    [BsonElement("tracerId")]
    public string TracerId { get; set; } = string.Empty;
}
