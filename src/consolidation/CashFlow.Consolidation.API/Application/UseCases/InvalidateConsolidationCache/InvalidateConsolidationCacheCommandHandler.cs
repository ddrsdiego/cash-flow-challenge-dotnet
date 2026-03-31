namespace CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

public sealed class InvalidateConsolidationCacheCommandHandler :
    IRequestHandler<InvalidateConsolidationCacheCommand, Response>
{
    private readonly IConsolidationCache _cache;
    private readonly ILogger<InvalidateConsolidationCacheCommandHandler> _logger;

    public InvalidateConsolidationCacheCommandHandler(
        IConsolidationCache cache,
        ILogger<InvalidateConsolidationCacheCommandHandler> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Response> Handle(InvalidateConsolidationCacheCommand request, CancellationToken cancellationToken)
    {
        InvalidateConsolidationCacheLog.InvalidatingCache(_logger, request.TracerId, request.ConsolidationKeys.Count);

        // ═══════════════════════════════════════════════════════════════════════════
        // FASE 1 - VALIDAR INPUTS
        // ═══════════════════════════════════════════════════════════════════════════
        if (request.ConsolidationKeys.Count == 0)
            return InvalidateConsolidationCacheErrors.EmptyConsolidationKeys(request.TracerId);

        try
        {
            // ═══════════════════════════════════════════════════════════════════════
            // FASE 2 + 3 - INVALIDAR (sem dependências externas, apenas in-memory)
            // ═══════════════════════════════════════════════════════════════════════
            var invalidatedCount = 0;

            foreach (var keyStr in request.ConsolidationKeys)
            {
                if (!ConsolidationKey.TryParse(keyStr, out var key))
                    continue;

                try
                {
                    await _cache.InvalidateAsync(key, cancellationToken);
                    invalidatedCount++;
                }
                catch (Exception ex)
                {
                    InvalidateConsolidationCacheLog.FailedToInvalidateCache(_logger, ex, request.TracerId);
                    return InvalidateConsolidationCacheErrors.CacheError(request.TracerId, ex.Message);
                }
            }

            InvalidateConsolidationCacheLog.CacheInvalidatedSuccessfully(_logger, request.TracerId, invalidatedCount);

            return Response.Ok();
        }
        catch (Exception ex)
        {
            InvalidateConsolidationCacheLog.UnexpectedError(_logger, ex, request.TracerId, ex.Message);
            return InvalidateConsolidationCacheErrors.UnexpectedError(request.TracerId, ex.Message);
        }
    }
}