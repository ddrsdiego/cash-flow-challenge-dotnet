using CashFlow.SharedKernel.DTOs.Requests;
using CashFlow.Transactions.API.Application.UseCases.CreateTransaction;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CashFlow.Transactions.API.Endpoints.Transactions.Extensions;

public static class CreateTransactionRequestEx
{
    public static CreateTransactionCommand ToCommand(this CreateTransactionRequest request, HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub")
                     ?? string.Empty;
        
        var tracerId = context.TraceIdentifier;
        var command = new CreateTransactionCommand(
            tracerId,
            userId,
            request.Type,
            request.Amount,
            request.Description,
            request.Category,
            request.Date);

        return command;
    }
}