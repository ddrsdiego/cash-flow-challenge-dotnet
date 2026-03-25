using CashFlow.SharedKernel.Domain.Entities;

namespace CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;

using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class GetDailyConsolidationQueryHandler :
    IRequestHandler<GetDailyConsolidationQuery, Response>
{
    private readonly IConsolidationCache _cache;
    private readonly IConsolidationQueryRepository _consolidationRepository;
    private readonly ILogger<GetDailyConsolidationQueryHandler> _logger;

    public GetDailyConsolidationQueryHandler(
        IConsolidationCache cache,
        IConsolidationQueryRepository consolidationRepository,
        ILogger<GetDailyConsolidationQueryHandler> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _consolidationRepository = consolidationRepository ?? throw new ArgumentNullException(nameof(consolidationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(GetDailyConsolidationQuery request, CancellationToken cancellationToken)
    {
        GetDailyConsolidationLog.FetchingConsolidation(_logger, request.TracerId, request.UserId, request.Date);

        // ═══════════════════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════════════════
        if (string.IsNullOrWhiteSpace(request.UserId))
            return GetDailyConsolidationErrors.InvalidUserId(request.TracerId);

        if (request.Date == default)
            return GetDailyConsolidationErrors.InvalidDate(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════════════════
            // FASE 2 - RESOLVER DEPENDÊNCIAS (Cache-First pattern)
            // ═══════════════════════════════════════════════════════════════════════
            var key = new ConsolidationKey(request.UserId, request.Date.Date);

            // 2.1 - Try cache first (ValueTask, zero-allocation on hit)
            var cachedResult = await _cache.GetAsync(key, cancellationToken);
            if (cachedResult.HasValue)
            {
                GetDailyConsolidationLog.CacheHit(_logger, request.TracerId, request.UserId, request.Date);
                GetDailyConsolidationLog.ConsolidationRetrieved(_logger, request.TracerId);
                return Response.Ok(cachedResult.Value);
            }

            // 2.2 - Cache miss — fetch from MongoDB
            GetDailyConsolidationLog.CacheMiss(_logger, request.TracerId, request.UserId, request.Date);

            var consolidationResult = await GetConsolidationFromDatabaseAsync(request, key, cancellationToken);
            if (consolidationResult.IsFailure)
                return consolidationResult.Error;

            var consolidation = consolidationResult.Value;

            // 2.3 - Map to response and populate cache
            var response = new DailyConsolidationResponse(
                consolidation.Date,
                consolidation.TotalCredits,
                consolidation.TotalDebits,
                consolidation.Balance,
                consolidation.TransactionCount,
                consolidation.LastUpdated);

            var today = DateTime.UtcNow.Date;
            var ttl = consolidation.Date == today
                ? TimeSpan.FromMinutes(5)
                : TimeSpan.FromHours(24);

            await _cache.SetAsync(key, response, ttl, cancellationToken);

            GetDailyConsolidationLog.ConsolidationRetrieved(_logger, request.TracerId);

            return Response.Ok(response);
        }
        catch (Exception ex)
        {
            GetDailyConsolidationLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return GetDailyConsolidationErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }

    private async Task<Result<DailyBalances, Response>> GetConsolidationFromDatabaseAsync(
        GetDailyConsolidationQuery request,
        ConsolidationKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            var consolidation = await _consolidationRepository.FindByKeyAsync(key, cancellationToken);
            if (consolidation.HasNoValue)
                return Result.Failure<DailyBalances, Response>(
                    GetDailyConsolidationErrors.ConsolidationNotFound(request.TracerId));

            return Result.Success<DailyBalances, Response>(consolidation.Value);
        }
        catch (Exception e)
        {
            GetDailyConsolidationLog.FailedToGetConsolidation(_logger, e, request.TracerId);
            return Result.Failure<DailyBalances, Response>(
                GetDailyConsolidationErrors.DatabaseError(request.TracerId, e.Message));
        }
    }
}
