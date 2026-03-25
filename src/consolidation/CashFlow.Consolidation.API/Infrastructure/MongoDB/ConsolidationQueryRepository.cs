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

namespace CashFlow.Consolidation.API.Infrastructure.MongoDB;

/// <summary>
/// Read-only repository for the Consolidation API to query daily consolidations from MongoDB.
/// Implements only IConsolidationQueryRepository interface (ISP) — no write operations.
/// Uses ConsolidationApiDbContext for dependency injection (follows established pattern).
/// </summary>
public sealed class ConsolidationQueryRepository : IConsolidationQueryRepository
{
    private readonly ConsolidationApiDbContext _context;

    public ConsolidationQueryRepository(ConsolidationApiDbContext context)
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
}
