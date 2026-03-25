using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Interfaces;
using MassTransit;
using MassTransit.MongoDbIntegration;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Infrastructure.Messaging;

/// <summary>
/// Centralized implementation of ITransactionalPublisher.
/// Abstracts the MongoDB session lifecycle managed by MassTransit's MongoDbContext.
/// In Consumer scope with UseMongoDbOutbox: session is already active, managed by MassTransit.
/// In HTTP scope: BeginTransactionAsync / CommitTransactionAsync must be called explicitly.
/// </summary>
public sealed class TransactionalPublisher : ITransactionalPublisher
{
    private readonly MongoDbContext _dbContext;
    private readonly IPublishEndpoint _publishEndpoint;

    public TransactionalPublisher(MongoDbContext dbContext, IPublishEndpoint publishEndpoint)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">Thrown if accessed when no transaction is active.</exception>
    public IClientSessionHandle Session => _dbContext.Session;

    /// <inheritdoc/>
    public Task BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        _dbContext.BeginTransaction(cancellationToken);

    /// <inheritdoc/>
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default) =>
        _dbContext.CommitTransaction(cancellationToken);

    /// <inheritdoc/>
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default) where T : class =>
        _publishEndpoint.Publish(message, cancellationToken);
}
