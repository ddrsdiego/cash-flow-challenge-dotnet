using System.Collections.Generic;
using MassTransit;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Event published when a batch of new financial transactions is successfully created.
/// Contains a batch of transactions to be processed by the Consolidation Worker
/// to recalculate daily balance and consolidation data.
/// </summary>
[MessageUrn("transaction-created")]
public sealed record TransactionCreatedEvent(
    string BatchId,
    string TracerId,
    IReadOnlyList<TransactionItem> Transactions);
