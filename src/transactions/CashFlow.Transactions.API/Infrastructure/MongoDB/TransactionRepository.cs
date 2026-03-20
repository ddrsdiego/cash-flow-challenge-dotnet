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

namespace CashFlow.Transactions.API.Infrastructure.MongoDB;

public class TransactionRepository : ITransactionRepository
{
    private readonly MongoDbContext _context;

    public TransactionRepository(MongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Maybe<Transaction>> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<Transaction>.Filter.Eq(t => t.Id, id);
        var transaction = await _context.Transactions
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return transaction == null
            ? Maybe<Transaction>.None
            : Maybe<Transaction>.From(transaction);
    }

    public async Task<IReadOnlyCollection<Transaction>> GetByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Transaction>.Filter;
        var filter = filterBuilder.Gte(t => t.Date, startDate.Date) &
                     filterBuilder.Lt(t => t.Date, endDate.Date.AddDays(1));

        if (type.HasValue)
            filter &= filterBuilder.Eq(t => t.Type, type.Value);

        var skip = (page - 1) * pageSize;

        var transactions = await _context.Transactions
            .Find(filter)
            .SortByDescending(t => t.Date)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return transactions.AsReadOnly();
    }

    public async Task<long> CountByPeriodAsync(
        DateTime startDate,
        DateTime endDate,
        TransactionType? type,
        CancellationToken cancellationToken = default)
    {
        var filterBuilder = Builders<Transaction>.Filter;
        var filter = filterBuilder.Gte(t => t.Date, startDate.Date) &
                     filterBuilder.Lt(t => t.Date, endDate.Date.AddDays(1));

        if (type.HasValue)
            filter &= filterBuilder.Eq(t => t.Type, type.Value);

        return await _context.Transactions
            .CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    public async Task InsertAsync(
        IEnumerable<Transaction> transactions,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var models = transactions
            .Select(t => new InsertOneModel<Transaction>(t))
            .ToList();

        var options = new BulkWriteOptions { IsOrdered = false };

        if (session != null)
            await _context.Transactions.BulkWriteAsync(session, models, options, cancellationToken);
        else
            await _context.Transactions.BulkWriteAsync(models, options, cancellationToken);
    }
}
