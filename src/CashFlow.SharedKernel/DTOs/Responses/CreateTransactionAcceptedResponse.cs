namespace CashFlow.SharedKernel.DTOs.Responses;

/// <summary>
/// Response DTO for 202 Accepted status.
/// Contains tracking information for async request processing.
/// </summary>
public sealed record CreateTransactionAcceptedResponse(
    string RequestId,
    string IdempotencyKey,
    string Status);
