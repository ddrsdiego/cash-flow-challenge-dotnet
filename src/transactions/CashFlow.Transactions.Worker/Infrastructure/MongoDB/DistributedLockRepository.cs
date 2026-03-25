using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Repository for distributed locking via MongoDB.
/// Ensures only one Worker instance performs batch polling and dispatching at a time.
/// </summary>
public class DistributedLockRepository : IDistributedLockRepository
{
    private readonly ITransactionsWorkerMongoDbContext _context;

    public DistributedLockRepository(ITransactionsWorkerMongoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Maybe<DistributedLock>> TryAcquireAsync(
        string lockId,
        string instanceId,
        int ttlSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds);

        // Atomic findOneAndUpdate: acquire or renew lock
        // Matches: _id = lockId AND (expiresAt < now OR heldBy = instanceId)
        var filter = Builders<DistributedLock>.Filter.And(
            Builders<DistributedLock>.Filter.Eq(l => l.Id, lockId),
            Builders<DistributedLock>.Filter.Or(
                Builders<DistributedLock>.Filter.Lt(l => l.ExpiresAt, DateTime.UtcNow),
                Builders<DistributedLock>.Filter.Eq(l => l.HeldBy, instanceId)
            )
        );

        var update = Builders<DistributedLock>.Update
            .Set(l => l.HeldBy, instanceId)
            .Set(l => l.AcquiredAt, DateTime.UtcNow)
            .Set(l => l.ExpiresAt, expiresAt);

        var options = new FindOneAndUpdateOptions<DistributedLock>
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };

        var acquiredLock = await _context.DistributedLocks
            .FindOneAndUpdateAsync(filter, update, options, cancellationToken);

        return acquiredLock == null
            ? Maybe<DistributedLock>.None
            : Maybe<DistributedLock>.From(acquiredLock);
    }

    public async Task<bool> IsHeldByAsync(
        string lockId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DistributedLock>.Filter.And(
            Builders<DistributedLock>.Filter.Eq(l => l.Id, lockId),
            Builders<DistributedLock>.Filter.Eq(l => l.HeldBy, instanceId),
            Builders<DistributedLock>.Filter.Gt(l => l.ExpiresAt, DateTime.UtcNow)
        );

        var result = await _context.DistributedLocks
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);

        return result != null;
    }

    public async Task ReleaseAsync(
        string lockId,
        string instanceId,
        CancellationToken cancellationToken)
    {
        var filter = Builders<DistributedLock>.Filter.And(
            Builders<DistributedLock>.Filter.Eq(l => l.Id, lockId),
            Builders<DistributedLock>.Filter.Eq(l => l.HeldBy, instanceId)
        );

        await _context.DistributedLocks.DeleteOneAsync(filter, cancellationToken: cancellationToken);
    }
}
