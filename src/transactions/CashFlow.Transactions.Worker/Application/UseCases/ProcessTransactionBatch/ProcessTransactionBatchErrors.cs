using Microsoft.AspNetCore.Http;
using CashFlow.SharedKernel.Application.Utils;

namespace CashFlow.Transactions.Worker.Application.UseCases.ProcessTransactionBatch;

public static class ProcessTransactionBatchErrors
{
    private const string Instance = "/ProcessTransactionBatch";

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
    // ERROS 404 - Recurso não encontrado (SEM RETRY)
    // ═════════════════════════════════════════════════════════════════════════════

    public static Response RawRequestsNotFound(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status404NotFound)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("RAW_REQUESTS_NOT_FOUND", "RAW_REQUESTS_NOT_FOUND", "Raw requests for batch not found")
                    .Build())
            .Build();

    // ═════════════════════════════════════════════════════════════════════════════
    // ERROS 500 - Infraestrutura (COM RETRY)
    // ═════════════════════════════════════════════════════════════════════════════

    public static Response DatabaseError(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("DATABASE_ERROR", "DATABASE_ERROR", $"Database error: {error}")
                    .Build())
            .Build();

    public static Response PersistenceError(string tracerId, string error) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status500InternalServerError)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("PERSISTENCE_ERROR", "PERSISTENCE_ERROR", $"Persistence error: {error}")
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
