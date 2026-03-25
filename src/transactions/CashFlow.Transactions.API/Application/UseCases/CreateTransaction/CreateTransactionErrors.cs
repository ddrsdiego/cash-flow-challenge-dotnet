using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Transactions.API.Application.UseCases.CreateTransaction;

internal static class CreateTransactionErrors
{
    private const string Instance = "/CreateTransaction";

    public static Response InvalidAmount(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_AMOUNT", "INVALID_AMOUNT", "Amount must be greater than zero")
                    .Build())
            .Build();

    public static Response EmptyDescription(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("EMPTY_DESCRIPTION", "EMPTY_DESCRIPTION", "Description is required")
                    .Build())
            .Build();

    public static Response DescriptionTooLong(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("DESCRIPTION_TOO_LONG", "DESCRIPTION_TOO_LONG", "Description must not exceed 500 characters")
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
                    .WithError("INVALID_DATE", "INVALID_DATE", "Date cannot be more than one day in the future")
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
