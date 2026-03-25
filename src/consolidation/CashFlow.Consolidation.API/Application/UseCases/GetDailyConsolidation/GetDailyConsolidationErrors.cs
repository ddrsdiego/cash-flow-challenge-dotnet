namespace CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;

using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

public static class GetDailyConsolidationErrors
{
    private const string Instance = "/GetDailyConsolidation";

    public static Response InvalidUserId(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_USER_ID", "INVALID_USER_ID", "User ID is required")
                    .Build())
            .Build();

    public static Response InvalidDate(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_DATE", "INVALID_DATE", "Invalid or missing date")
                    .Build())
            .Build();

    public static Response ConsolidationNotFound(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status404NotFound)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("CONSOLIDATION_NOT_FOUND", "CONSOLIDATION_NOT_FOUND", "Daily consolidation not found for the specified date")
                    .Build())
            .Build();

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
