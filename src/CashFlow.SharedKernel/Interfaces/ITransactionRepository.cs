using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Repository interface for the Transaction aggregate.
/// Implementations are in the CashFlow.Transactions.API infrastructure layer.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Retrieves a transaction by its unique identifier.
    /// Returns Maybe.None if not found.
    /// </summary>
    Task<Maybe<Transaction>> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all transactions within the specified date range, with optional type filter.
    /// Returns an empty collection if none found.
    /// </summary>
    Task<IReadOnlyCollection<Transaction>> GetByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of transactions within the specified date range.
    /// Used for pagination metadata.
    /// </summary>
    Task<long> CountByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts one or more transactions in batch using BulkWrite.
    /// Accepts an optional IClientSessionHandle to participate in a MongoDB transaction.
    /// Use new[] { transaction } for single inserts to keep the interface batch-safe.
    /// </summary>
    Task InsertAsync(
        IEnumerable<Transaction> transactions,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default);
}
