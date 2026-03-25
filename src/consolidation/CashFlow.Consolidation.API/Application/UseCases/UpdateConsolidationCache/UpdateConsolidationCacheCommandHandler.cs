namespace CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class UpdateConsolidationCacheCommandHandler :
    IRequestHandler<UpdateConsolidationCacheCommand, Response>
{
    private readonly IConsolidationQueryRepository _consolidationRepository;
    private readonly IConsolidationCache _cache;
    private readonly ILogger<UpdateConsolidationCacheCommandHandler> _logger;

    public UpdateConsolidationCacheCommandHandler(
        IConsolidationQueryRepository consolidationRepository,
        IConsolidationCache cache,
        ILogger<UpdateConsolidationCacheCommandHandler> logger)
    {
        _consolidationRepository = consolidationRepository ?? throw new ArgumentNullException(nameof(consolidationRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(UpdateConsolidationCacheCommand request, CancellationToken cancellationToken)
    {
        UpdateConsolidationCacheLog.UpdatingCache(_logger, request.TracerId, request.ConsolidationKeys.Count);

        // ═══════════════════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.ConsolidationKeys.Count == 0)
            return UpdateConsolidationCacheErrors.EmptyConsolidationKeys(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS (buscar consolidações e construir map)
            // ═══════════════════════════════════════════════════════════════════════
            var consolidationKeysResult = await GetConsolidationsByKeysAsync(request, cancellationToken);
            if (consolidationKeysResult.IsFailure)
                return consolidationKeysResult.Error;

            var consolidations = consolidationKeysResult.Value;
            var foundKeys = new HashSet<string>(consolidations.Select(c => c.ConsolidationKey));
            var notFoundKeys = request.ConsolidationKeys
                .Where(k => !foundKeys.Contains(k))
                .ToList();

            // ═══════════════════════════════════════════════════════════════════════
            // FASE 3 - PERSISTIR (atualizar cache)
            // ═══════════════════════════════════════════════════════════════════════
            var updateCacheResult = await UpdateCacheAsync(request, consolidations, notFoundKeys, cancellationToken);
            if (updateCacheResult.IsFailure)
                return updateCacheResult;

            UpdateConsolidationCacheLog.CacheUpdated(_logger, request.TracerId, foundKeys.Count, notFoundKeys.Count);

            return Response.Ok();
        }
        catch (Exception ex)
        {
            UpdateConsolidationCacheLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return UpdateConsolidationCacheErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    private async Task<Result<IReadOnlyCollection<DailyBalances>, Response>> GetConsolidationsByKeysAsync(
        UpdateConsolidationCacheCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var consolidationKeys = ParseConsolidationKeys(request.ConsolidationKeys);

            if (consolidationKeys.Count == 0)
            {
                UpdateConsolidationCacheLog.CacheUpdated(_logger, request.TracerId, 0, request.ConsolidationKeys.Count);
                return Result.Success<IReadOnlyCollection<DailyBalances>, Response>(Array.Empty<DailyBalances>());
            }

            var consolidations = await _consolidationRepository.FindDailyConsolidationsByKeysAsync(
                consolidationKeys,
                cancellationToken);

            return Result.Success<IReadOnlyCollection<DailyBalances>, Response>(consolidations);
        }
        catch (Exception e)
        {
            UpdateConsolidationCacheLog.FailedToGetConsolidations(_logger, e, request.TracerId);
            return Result.Failure<IReadOnlyCollection<DailyBalances>, Response>(
                UpdateConsolidationCacheErrors.DatabaseError(request.TracerId, e.Message));
        }
    }

    private static List<ConsolidationKey> ParseConsolidationKeys(IReadOnlyList<string> keys)
    {
        var result = new List<ConsolidationKey>();

        foreach (var key in keys)
        {
            if (ConsolidationKey.TryParse(key, out var consolidationKey))
                result.Add(consolidationKey);
        }

        return result;
    }

    private async Task<Response> UpdateCacheAsync(
        UpdateConsolidationCacheCommand request,
        IReadOnlyCollection<DailyBalances> consolidations,
        List<string> notFoundKeys,
        CancellationToken cancellationToken)
    {
        try
        {
            var today = DateTime.UtcNow.Date;

            // Update cache for found consolidations
            foreach (var consolidation in consolidations)
            {
                var key = new ConsolidationKey(consolidation.UserId, consolidation.Date);
                var ttl = consolidation.Date == today
                    ? TimeSpan.FromMinutes(5)
                    : TimeSpan.FromHours(24);

                var response = new DailyConsolidationResponse(
                    consolidation.Date,
                    consolidation.TotalCredits,
                    consolidation.TotalDebits,
                    consolidation.Balance,
                    consolidation.TransactionCount,
                    consolidation.LastUpdated);

                await _cache.SetAsync(key, response, ttl, cancellationToken);
            }

            // Invalidate cache for keys not found in database
            foreach (var keyStr in notFoundKeys)
            {
                if (!ConsolidationKey.TryParse(keyStr, out var key))
                    continue;
                
                await _cache.InvalidateAsync(key, cancellationToken);
            }

            return Response.Ok();
        }
        catch (Exception e)
        {
            UpdateConsolidationCacheLog.FailedToUpdateCache(_logger, e, request.TracerId, e.Message);
            return UpdateConsolidationCacheErrors.CacheError(request.TracerId, e.Message);
        }
    }
}
