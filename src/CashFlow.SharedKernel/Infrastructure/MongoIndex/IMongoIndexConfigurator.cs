using System.Threading.Tasks;

namespace CashFlow.SharedKernel.Infrastructure.MongoIndex;

/// <summary>
/// Marker interface for MongoDB index configuration.
/// Implementations create and ensure indexes exist for specific collections.
/// Used for dependency injection and startup initialization.
/// </summary>
public interface IMongoIndexConfigurator
{
    /// <summary>
    /// Ensures all required indexes exist for the associated MongoDB collection.
    /// This method is idempotent and safe to call multiple times.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task EnsureIndexesAsync();
}
