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
/// Inherits write operations from ITransactionWriteRepository for segregated interfaces.
/// </summary>
public interface ITransactionRepository : ITransactionWriteRepository
{
    /// <summary>
    /// Retrieves a transaction by its unique identifier and user ID.
    /// Returns Maybe.None if not found.
    /// </summary>
    Task<Maybe<Transaction>> GetByIdAsync(string id, string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all transactions within the specified date range, with optional type and user ID filters.
    /// Returns an empty collection if none found.
    /// </summary>
    Task<IReadOnlyCollection<Transaction>> GetByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        string? userId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of transactions within the specified date range, with optional type and user ID filters.
    /// Used for pagination metadata.
    /// </summary>
    Task<long> CountByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
