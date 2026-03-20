using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Transactions.API.Application.UseCases.ListTransactions;

internal static class ListTransactionsErrors
{
    private const string Instance = "/ListTransactions";

    public static Response InvalidDateRange(string tracerId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status400BadRequest)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("INVALID_DATE_RANGE", "INVALID_DATE_RANGE", "StartDate must be less than or equal to EndDate")
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
}
