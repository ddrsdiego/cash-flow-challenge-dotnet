using System.Collections.Generic;

namespace CashFlow.SharedKernel.DTOs.Responses;

/// <summary>
/// Represents a paginated result set for list queries.
/// </summary>
public sealed record PagedResult<T>(
    int Page,
    int PageSize,
    long Total,
    IReadOnlyCollection<T> Data);
