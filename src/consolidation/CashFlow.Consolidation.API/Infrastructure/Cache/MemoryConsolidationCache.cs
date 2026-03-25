using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;

namespace CashFlow.Consolidation.API.Infrastructure.Cache;

public sealed class MemoryConsolidationCache : IConsolidationCache
{
    private const string CacheKeyPrefix = "consol:";
    private readonly IMemoryCache _memoryCache;

    public MemoryConsolidationCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    public ValueTask<Maybe<DailyConsolidationResponse>> GetAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = FormatCacheKey(key);
        var success = _memoryCache.TryGetValue(cacheKey, out DailyConsolidationResponse response);
        return ValueTask.FromResult(success 
            ? Maybe<DailyConsolidationResponse>.From(response) 
            : Maybe<DailyConsolidationResponse>.None);
    }

    public ValueTask SetAsync(
        ConsolidationKey key,
        DailyConsolidationResponse response,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = FormatCacheKey(key);
        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        _memoryCache.Set(cacheKey, response, options);
        return ValueTask.CompletedTask;
    }

    public ValueTask InvalidateAsync(
        ConsolidationKey key,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = FormatCacheKey(key);
        _memoryCache.Remove(cacheKey);
        return ValueTask.CompletedTask;
    }

    private static string FormatCacheKey(ConsolidationKey key) => $"{CacheKeyPrefix}{key.Value}";
}
