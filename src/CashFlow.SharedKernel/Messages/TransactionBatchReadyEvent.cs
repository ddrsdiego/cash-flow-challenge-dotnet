using System.Collections.Generic;
using MassTransit;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Event published when a batch of raw transaction requests is ready for processing.
/// Consumed by the Transactions Worker Processor to validate and persist transactions.
/// Published by the Batcher after marking raw requests as dispatched.
/// Queue: transaction.batch.process
/// </summary>
[MessageUrn("transaction-batch-ready")]
public sealed record TransactionBatchReadyEvent(
    string BatchId,
    string TracerId,
    IReadOnlyList<string> RawRequestIds);
