using MassTransit;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Internal event published after a batch of transactions has been successfully received and persisted.
/// Consumed by the Consolidation Worker to trigger batch processing.
/// Contains only BatchId and TracerId for minimal payload.
/// </summary>
[MessageUrn("consolidation-batch-received")]
public sealed record ConsolidationBatchReceivedEvent(
    string BatchId,
    string TracerId);
