using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

public class ReceivedTransactionRepository : IReceivedTransactionRepository
{
    private readonly MongoDbContext _context;

    public ReceivedTransactionRepository(MongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task BulkInsertAsync(
        IEnumerable<ReceivedTransaction> transactions,
        CancellationToken cancellationToken)
    {
        var models = transactions
            .Select(t => new InsertOneModel<ReceivedTransaction>(t))
            .ToList();

        var options = new BulkWriteOptions { IsOrdered = false };

        await _context.ReceivedTransactions.BulkWriteAsync(models, options, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ReceivedTransaction>> GetByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ReceivedTransaction>.Filter.Eq(t => t.BatchId, batchId);
        var transactions = await _context.ReceivedTransactions
            .Find(filter)
            .ToListAsync(cancellationToken);

        return transactions.AsReadOnly();
    }

    public async Task DeleteByBatchIdAsync(
        string batchId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ReceivedTransaction>.Filter.Eq(t => t.BatchId, batchId);
        await _context.ReceivedTransactions.DeleteManyAsync(filter, cancellationToken: cancellationToken);
    }
}
