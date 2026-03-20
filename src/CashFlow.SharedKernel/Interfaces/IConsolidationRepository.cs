using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository interface for the DailyConsolidation aggregate.
/// Implementations are in the CashFlow.Consolidation infrastructure layer.
/// </summary>
public interface IConsolidationRepository
{
    /// <summary>
    /// Retrieves the daily consolidation for the specified date.
    /// Returns Maybe.None if no transactions have been consolidated for that date.
    /// </summary>
    Task<Maybe<DailyConsolidation>> GetByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the daily consolidation document.
    /// If a document for the given date already exists, it is replaced.
    /// Accepts an optional IClientSessionHandle to participate in a MongoDB transaction.
    /// </summary>
    Task UpsertAsync(
        DailyConsolidation consolidation,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);
}
