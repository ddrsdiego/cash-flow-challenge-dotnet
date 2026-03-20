using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

public class ConsolidationRepository : IConsolidationRepository
{
    private readonly MongoDbContext _context;

    public ConsolidationRepository(MongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Maybe<DailyConsolidation>> GetByDateAsync(
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var dateOnly = date.Date;
        var filter = Builders<DailyConsolidation>.Filter.Eq(c => c.Date, dateOnly);
        var consolidation = await _context.DailyConsolidations
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return consolidation == null
            ? Maybe<DailyConsolidation>.None
            : Maybe<DailyConsolidation>.From(consolidation);
    }

    public async Task UpsertAsync(
        DailyConsolidation consolidation,
        IClientSessionHandle session = null,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<DailyConsolidation>.Filter.Eq(c => c.Date, consolidation.Date);
        var options = new ReplaceOptions { IsUpsert = true };

        if (session != null)
            await _context.DailyConsolidations.ReplaceOneAsync(session, filter, consolidation, options, cancellationToken);
        else
            await _context.DailyConsolidations.ReplaceOneAsync(filter, consolidation, options, cancellationToken);
    }
}
