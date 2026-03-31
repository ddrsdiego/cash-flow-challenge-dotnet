using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Repository for Transaction persistence in Worker context.
/// Used by ProcessTransactionBatch to bulk insert validated transactions.
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly ITransactionsWorkerMongoDbContext _context;

    public TransactionRepository(ITransactionsWorkerMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Maybe<Transaction>> GetByIdAsync(string id, string? userId = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Not used in Worker");
    }

    public Task<IReadOnlyCollection<Transaction>> GetByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        string? userId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Not used in Worker");
    }

    public Task<long> CountByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Not used in Worker");
    }

    public async Task InsertAsync(
        IEnumerable<Transaction> transactions,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var transactionList = transactions.ToList();

        if (transactionList.Count == 0)
            return;

        var models = transactionList
            .Select(t => new InsertOneModel<Transaction>(t))
            .ToList();

        var options = new BulkWriteOptions { IsOrdered = false };

        if (session != null)
            await _context.Transactions.BulkWriteAsync(session, models, options, cancellationToken);
        else
            await _context.Transactions.BulkWriteAsync(models, options, cancellationToken);
    }
}
