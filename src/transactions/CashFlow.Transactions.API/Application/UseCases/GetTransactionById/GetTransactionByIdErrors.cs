using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;

namespace CashFlow.Transactions.API.Application.UseCases.GetTransactionById;

internal static class GetTransactionByIdErrors
{
    private const string Instance = "/GetTransactionById";

    public static Response TransactionNotFound(string tracerId, string transactionId) =>
        Response.Builder()
            .WithRequestId(tracerId)
            .WithStatusCode(StatusCodes.Status404NotFound)
            .WithErrorResponse(
                ErrorResponse.Builder()
                    .WithInstance(Instance)
                    .WithTraceId(tracerId)
                    .WithError("TRANSACTION_NOT_FOUND", "TRANSACTION_NOT_FOUND",
                        $"Transaction '{transactionId}' not found")
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
