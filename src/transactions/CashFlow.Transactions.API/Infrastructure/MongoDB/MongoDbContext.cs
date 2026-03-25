using System;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CashFlow.Transactions.API.Infrastructure.MongoDB;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IMongoClient client, IConfiguration configuration)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        var databaseName = configuration["MongoDB:DatabaseName"]
            ?? throw new InvalidOperationException("MongoDB:DatabaseName configuration is missing.");

        _database = client.GetDatabase(databaseName);
    }

    public IMongoCollection<Transaction> Transactions =>
        _database.GetCollection<Transaction>("transactions");

    public IMongoCollection<RawRequest> RawRequests =>
        _database.GetCollection<RawRequest>("raw_requests");
}
