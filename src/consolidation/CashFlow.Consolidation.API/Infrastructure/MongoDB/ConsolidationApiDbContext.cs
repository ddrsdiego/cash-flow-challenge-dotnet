using System;
using CashFlow.SharedKernel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace CashFlow.Consolidation.API.Infrastructure.MongoDB;

/// <summary>
/// MongoDB database context for Consolidation API read operations.
/// Centralizes collection definitions following the pattern established in Transactions API.
/// </summary>
public sealed class ConsolidationApiDbContext
{
    public IMongoCollection<DailyBalances> DailyBalances { get; }

    public ConsolidationApiDbContext(IMongoClient client, IConfiguration configuration)
    {
        var mongoClient = client ?? throw new ArgumentNullException(nameof(client));
        var config = configuration ?? throw new ArgumentNullException(nameof(configuration));

        var databaseName = config["MongoDB:DatabaseName"] 
                           ?? throw new InvalidOperationException("MongoDB:DatabaseName is required in configuration");

        var database = mongoClient.GetDatabase(databaseName);
        DailyBalances = database.GetCollection<DailyBalances>("daily_balances");
    }
}
