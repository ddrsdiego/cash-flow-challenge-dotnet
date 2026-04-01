using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Write-only repository interface for transactions.
/// Segregated from read operations to follow Interface Segregation Principle.
/// </summary>
public interface ITransactionWriteRepository
{
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
