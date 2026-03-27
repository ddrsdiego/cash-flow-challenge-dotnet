using System;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CashFlow.Transactions.Worker.Infrastructure.MongoDB;

/// <summary>
/// Application-level MongoDB context for Transactions Worker.
/// Provides access to collections: raw_requests, distributed_locks, transactions.
/// Separate from MassTransit's MongoDbContext which manages Outbox.
/// </summary>
public interface ITransactionsWorkerMongoDbContext
{
    IMongoCollection<RawRequest> RawRequests { get; }
    IMongoCollection<DistributedLock> DistributedLocks { get; }
    IMongoCollection<Transaction> Transactions { get; }
}

public sealed class TransactionsWorkerMongoDbContext :
    ITransactionsWorkerMongoDbContext
{
    private readonly IMongoDatabase _database;

    public TransactionsWorkerMongoDbContext(IMongoClient client, IConfiguration configuration)
    {
        const string databaseConfig = "MongoDB:DatabaseName";
        
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseName = configuration[databaseConfig]
                           ?? throw new InvalidOperationException("MongoDB:DatabaseName configuration is missing.");
        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<RawRequest> RawRequests =>
        _database.GetCollection<RawRequest>("raw_requests");

    public IMongoCollection<DistributedLock> DistributedLocks =>
        _database.GetCollection<DistributedLock>("distributed_locks");

    public IMongoCollection<Transaction> Transactions =>
        _database.GetCollection<Transaction>("transactions");
}
