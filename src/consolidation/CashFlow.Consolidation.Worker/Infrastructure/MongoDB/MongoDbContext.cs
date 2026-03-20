using System;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CashFlow.Consolidation.Worker.Infrastructure.MongoDB;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IMongoClient client, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);

        var databaseName = configuration["MongoDB:DatabaseName"]
            ?? throw new InvalidOperationException("MongoDB:DatabaseName configuration is missing.");

        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<ReceivedTransaction> ReceivedTransactions =>
        _database.GetCollection<ReceivedTransaction>("received_transactions");

    public IMongoCollection<DailyConsolidation> DailyConsolidations =>
        _database.GetCollection<DailyConsolidation>("daily_consolidation");
}
