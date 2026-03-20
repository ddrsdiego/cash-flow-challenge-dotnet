using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Interfaces;

/// <summary>
/// Abstracts the transaction lifecycle and event publishing for Commands that require atomicity.
/// Implementations manage a MongoDB session internally and publish events through MassTransit.
/// Lifetime: Scoped — one instance per HTTP request.
/// </summary>
public interface ITransactionalPublisher
{
    /// <summary>
    /// The active MongoDB session. Valid only after BeginTransactionAsync is called.
    /// Throws InvalidOperationException if accessed before BeginTransactionAsync.
    /// </summary>
    IClientSessionHandle Session { get; }

    /// <summary>
    /// Starts a MongoDB client session and begins a transaction.
    /// Must be called before accessing Session or persisting data.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the active transaction, persisting all changes atomically.
    /// If not called, the transaction will be aborted when Dispose is called at request end.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a message through MassTransit (MongoDB Outbox pattern when configured).
    /// Messages are only delivered after the transaction is committed.
    /// </summary>
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
}
