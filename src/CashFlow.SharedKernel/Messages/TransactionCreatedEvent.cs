using System;
using CashFlow.SharedKernel.Domain.Enums;
using MassTransit;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Event published when a new financial transaction is successfully created.
/// Consumed by the Consolidation Worker to recalculate the daily balance.
/// </summary>
[MessageUrn("transaction-created")]
public sealed record TransactionCreatedEvent(
    string TransactionId,
    string UserId,
    TransactionType Type,
    decimal Amount,
    Category Category,
    DateTime Date,
    string TracerId);
