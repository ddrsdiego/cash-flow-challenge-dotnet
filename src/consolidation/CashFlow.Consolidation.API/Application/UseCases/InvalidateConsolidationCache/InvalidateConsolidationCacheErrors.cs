namespace CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;

using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

public static class InvalidateConsolidationCacheErrors
{
    private const string Instance = "/InvalidateConsolidationCache";

    public static Response EmptyConsolidationKeys(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("EMPTY_CONSOLIDATION_KEYS", "EMPTY_CONSOLIDATION_KEYS", "No consolidation keys provided for cache invalidation")
                    .Build())
            .Build();

    public static Response CacheError(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("CACHE_ERROR", "CACHE_ERROR", $"Cache invalidation error: {error}")
                    .Build())
            .Build();

    public static Response UnexpectedError(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("UNEXPECTED_ERROR", "UNEXPECTED_ERROR", $"Unexpected error: {error}")
                    .Build())
            .Build();
}
