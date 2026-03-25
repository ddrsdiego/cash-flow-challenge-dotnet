using Microsoft.AspNetCore.Http;
using CashFlow.SharedKernel.Application.Utils;

namespace CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;

public static class DispatchTransactionBatchErrors
{
    private const string Instance = "/DispatchTransactionBatch";

    // ═════════════════════════════════════════════════════════════════════════════
    // ERROS 400 - Input inválido (SEM RETRY)
    // ═════════════════════════════════════════════════════════════════════════════

    public static Response EmptyBatch(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("EMPTY_BATCH", "EMPTY_BATCH", "Batch contains no requests")
                    .Build())
            .Build();

    public static Response InvalidBatchId(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_BATCH_ID", "INVALID_BATCH_ID", "BatchId is required")
                    .Build())
            .Build();

    // ═════════════════════════════════════════════════════════════════════════════
    // ERROS 400 - Validação (SEM RETRY)
    // ═════════════════════════════════════════════════════════════════════════════

    public static Response InvalidTracerId(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_TRACER_ID", "INVALID_TRACER_ID", "TracerId is required")
                    .Build())
            .Build();

    public static Response InvalidInstanceId(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_INSTANCE_ID", "INVALID_INSTANCE_ID", "InstanceId is required")
                    .Build())
            .Build();

    // ═════════════════════════════════════════════════════════════════════════════
    // ERROS 500 - Infraestrutura (COM RETRY)
    // ═════════════════════════════════════════════════════════════════════════════

    public static Response FailedToAcquireLock(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("LOCK_ERROR", "LOCK_ERROR", $"Failed to acquire distributed lock: {error}")
                    .Build())
            .Build();

    public static Response FailedToFindPendingRequests(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("QUERY_ERROR", "QUERY_ERROR", $"Failed to find pending requests: {error}")
                    .Build())
            .Build();

    public static Response DispatchError(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("DISPATCH_ERROR", "DISPATCH_ERROR", $"Failed to dispatch batch: {error}")
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
