using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace CashFlow.SharedKernel.Infrastructure.Extensions;

/// <summary>
/// Centralizes MongoDB client configuration and registration.
/// Each service registers its own repositories and contexts.
/// </summary>
public static class MongoDbExtensions
{
    /// <summary>
    /// Registers MongoDB IMongoClient singleton with connection string and authentication.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">The configuration object</param>
    /// <returns>The service collection for chaining</returns>
    /// <exception cref="InvalidOperationException">Thrown when MongoDB:ConnectionString is not configured</exception>
    public static IServiceCollection AddMongoDbClient(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["MongoDB:ConnectionString"]
                               ?? throw new InvalidOperationException("MongoDB:ConnectionString is required.");

        var username = configuration["MongoDB:Username"];
        var password = configuration["MongoDB:Password"];
        var authSource = configuration["MongoDB:AuthSource"];

        var settings = MongoClientSettings.FromConnectionString(connectionString);

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            settings.Credential = MongoCredential.CreateCredential(authSource, username, password);

        services.TryAddSingleton<IMongoClient>(_ => new MongoClient(settings));

        return services;
    }
}
