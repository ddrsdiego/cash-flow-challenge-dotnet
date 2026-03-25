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
    private readonly ICashFlowMongoDbContext _context;

    public ReceivedTransactionRepository(ICashFlowMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task BulkInsertAsync(
        IEnumerable<ReceivedTransaction> transactions,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var models = transactions
            .Select(t => new InsertOneModel<ReceivedTransaction>(t))
            .ToList();

        var options = new BulkWriteOptions { IsOrdered = false };

        if (session != null)
            await _context.ReceivedTransactions.BulkWriteAsync(session, models, options, cancellationToken);
        else
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
