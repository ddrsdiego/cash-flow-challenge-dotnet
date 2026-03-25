using System;
using System.Collections.Generic;
using MassTransit;

namespace CashFlow.SharedKernel.Messages;

/// <summary>
/// Event published after a batch of transactions has been processed and daily consolidations updated.
/// Consumed by downstream services to react to consolidation changes.
/// Contains BatchId for correlation, list of affected dates, and consolidation keys for cache invalidation.
/// The Consolidation API consumer uses ConsolidationKeys to:
///   1. Fetch updated consolidations in batch from MongoDB
///   2. Update local MemoryCache entries
///   3. Invalidate stale entries
/// </summary>
[MessageUrn("daily-consolidation-updated")]
public sealed record DailyConsolidationUpdatedEvent(
    string BatchId,
    string TracerId,
    IReadOnlyList<DateTime> ProcessedDates,
    IReadOnlyList<string> ConsolidationKeys); // e.g., ["userId1-20260320", "userId2-20260320"]
