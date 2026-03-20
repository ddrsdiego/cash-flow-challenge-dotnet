using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Interfaces;
using MassTransit;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Infrastructure.Messaging;

/// <summary>
/// Centralized implementation of ITransactionalPublisher.
/// Manages the MongoDB session lifecycle and publishes events via MassTransit.
/// The DI container calls Dispose() at end of request — any uncommitted transaction
/// is automatically aborted by the MongoDB driver when the session is disposed.
/// </summary>
public sealed class TransactionalPublisher : ITransactionalPublisher, IDisposable
{
    private readonly IMongoClient _mongoClient;
    private readonly IPublishEndpoint _publishEndpoint;
    private IClientSessionHandle _session;

    public TransactionalPublisher(IMongoClient mongoClient, IPublishEndpoint publishEndpoint)
    {
        _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Thrown if accessed before BeginTransactionAsync.</exception>
    public IClientSessionHandle Session =>
        _session ?? throw new InvalidOperationException(
            "Transaction not started. Call BeginTransactionAsync before accessing Session.");

    /// <inheritdoc/>
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        _session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);
        _session.StartTransaction();
    }

    /// <inheritdoc/>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        await _session.CommitTransactionAsync(cancellationToken);

    /// <inheritdoc/>
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class =>
        _publishEndpoint.Publish(message, cancellationToken);

    /// <summary>
    /// Disposes the MongoDB session.
    /// If a transaction is still open (CommitTransactionAsync was not called),
    /// the MongoDB driver will abort it automatically — ensuring no partial commits.
    /// </summary>
    public void Dispose() => _session?.Dispose();
}
