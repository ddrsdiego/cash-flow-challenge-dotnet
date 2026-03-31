using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CashFlow.SharedKernel.Infrastructure.MongoIndex;

/// <summary>
/// Initializes MongoDB indexes at application startup.
/// Discovers all IMongoIndexConfigurator implementations from DI
/// and executes index creation in parallel.
/// </summary>
[ExcludeFromCodeCoverage]
public static class MongoDbIndexesInitializer
{
    /// <summary>
    /// Initializes all MongoDB indexes registered in the dependency injection container.
    /// Executes index creation in parallel for all configurators.
    /// </summary>
    /// <param name="host">The host builder instance</param>
    /// <returns>The same host builder for chaining</returns>
    public static async Task<IHost> InitializeIndexesAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var configurators = scope.ServiceProvider.GetServices<IMongoIndexConfigurator>();

        var mongoIndexConfigurators = configurators.ToList();
        if (mongoIndexConfigurators.Count == 0)
            return host;

        var tasks = new List<Task>();
        foreach (var configurator in mongoIndexConfigurators)
        {
            tasks.Add(configurator.EnsureIndexesAsync());
        }

        await Task.WhenAll(tasks);
        return host;
    }
}
