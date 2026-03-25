namespace CashFlow.SharedKernel.Domain.Enums;

/// <summary>
/// Status of a raw transaction request in the batch processing pipeline.
/// </summary>
public enum RawRequestStatus
{
    /// <summary>
    /// Request received and persisted, awaiting batch processing.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Request included in a batch and dispatched to processing queue.
    /// </summary>
    Dispatched = 2,

    /// <summary>
    /// Request successfully processed and transactions persisted.
    /// </summary>
    Processed = 3,

    /// <summary>
    /// Processing failed after retries.
    /// </summary>
    Failed = 4
}
