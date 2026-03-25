using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

public class ConsolidationRepository :
    IConsolidationRepository
{
    private readonly ICashFlowMongoDbContext _context;

    public ConsolidationRepository(ICashFlowMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Maybe<DailyBalances>> FindByKeyAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<DailyBalances>.Filter.Eq(c => c.ConsolidationKey, key.Value);
        var consolidation = await _context.DailyBalances
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return consolidation == null ? Maybe<DailyBalances>.None : Maybe<DailyBalances>.From(consolidation);
    }

    public async Task<IReadOnlyCollection<DailyBalances>> FindDailyConsolidationsByKeysAsync(
        IEnumerable<ConsolidationKey> keys,
        CancellationToken cancellationToken = default)
    {
        var keysList = keys.ToList();

        if (keysList.Count == 0)
            return Array.Empty<DailyBalances>();

        var keyValues = keysList.Select(k => k.Value).ToList();
        var filter = Builders<DailyBalances>.Filter.In(c => c.ConsolidationKey, keyValues);

        var consolidations = await _context.DailyBalances
            .Find(filter)
            .ToListAsync(cancellationToken);

        return consolidations.AsReadOnly();
    }

    public async Task ApplyDailyConsolidationsAsync(
        IEnumerable<DailyBalances> consolidations,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var consolidationsList = consolidations.ToList();

        if (consolidationsList.Count == 0)
            return;

        var models = consolidationsList
            .Select(consolidation =>
                new ReplaceOneModel<DailyBalances>(
                    Builders<DailyBalances>.Filter.Eq(c => c.ConsolidationKey, consolidation.ConsolidationKey),
                    consolidation)
                {
                    IsUpsert = true
                })
            .Cast<WriteModel<DailyBalances>>()
            .ToList();

        var options = new BulkWriteOptions { IsOrdered = false };

        if (session == null)
            await _context.DailyBalances.BulkWriteAsync(models, options, cancellationToken);
        else
            await _context.DailyBalances.BulkWriteAsync(session, models, options, cancellationToken);
    }
}