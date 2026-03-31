namespace CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;

using System.Collections.Generic;
using MediatR;
using CashFlow.SharedKernel.Application.Utils;

public record InvalidateConsolidationCacheCommand(
    string TracerId,
    IReadOnlyList<string> ConsolidationKeys) : IRequest<Response>;
