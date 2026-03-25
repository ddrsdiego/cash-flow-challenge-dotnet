using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Consolidation.Worker.Application.UseCases.ProcessConsolidationBatch;

public static class ProcessConsolidationBatchErrors
{
    private const string Instance = "/ProcessConsolidationBatch";

    // ═══════════════════════════════════════════════════════════════════════════
    // ERROS 400 - Input inválido (SEM RETRY)
    // ═══════════════════════════════════════════════════════════════════════════

    public static Response InvalidBatchId(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_BATCH_ID", "INVALID_BATCH_ID", "BatchId cannot be null or empty")
                    .Build())
            .Build();

    // ═══════════════════════════════════════════════════════════════════════════
    // ERROS 500 - Infraestrutura (COM RETRY)
    // ═══════════════════════════════════════════════════════════════════════════

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
