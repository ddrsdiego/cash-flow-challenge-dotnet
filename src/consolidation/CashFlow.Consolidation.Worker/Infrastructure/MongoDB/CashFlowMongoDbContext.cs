using System;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

public interface ICashFlowMongoDbContext
{
    IMongoCollection<DailyBalances> DailyBalances { get; }
    
    IMongoCollection<ReceivedTransaction> ReceivedTransactions { get; }
}

/// <summary>
/// Application-level MongoDB context providing access to collections.
/// Separate from MassTransit's MongoDbContext which manages transactions for Outbox.
/// </summary>
public sealed class CashFlowMongoDbContext :
    ICashFlowMongoDbContext
{
    private readonly IMongoDatabase _database;

    public CashFlowMongoDbContext(IMongoClient client, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseName = configuration["MongoDB:DatabaseName"]
            ?? throw new InvalidOperationException("MongoDB:DatabaseName configuration is missing.");

        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<ReceivedTransaction> ReceivedTransactions =>
        _database.GetCollection<ReceivedTransaction>("received_transactions");

    public IMongoCollection<DailyBalances> DailyBalances =>
        _database.GetCollection<DailyBalances>("daily_balances");
}