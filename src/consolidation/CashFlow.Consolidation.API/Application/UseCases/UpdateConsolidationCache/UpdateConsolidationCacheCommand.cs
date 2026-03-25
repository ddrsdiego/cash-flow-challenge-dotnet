using System.Collections.Generic;
using MediatR;
using CashFlow.SharedKernel.Application.Utils;

namespace CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;

public record UpdateConsolidationCacheCommand(
    string TracerId,
    IReadOnlyList<string> ConsolidationKeys) : IRequest<Response>;
