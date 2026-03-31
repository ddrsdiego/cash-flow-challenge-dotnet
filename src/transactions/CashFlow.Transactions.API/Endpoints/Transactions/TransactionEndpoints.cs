namespace CashFlow.Transactions.API.Endpoints.Transactions;

using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.DTOs.Requests;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.Transactions.API.Application.UseCases.GetTransactionById;
using CashFlow.Transactions.API.Application.UseCases.ListTransactions;
using CashFlow.Transactions.API.Endpoints.Transactions.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transactions")
            .RequireAuthorization()
            .WithTags("Transactions");

        group.MapPost("/", CreateTransactionAsync)
            .WithName("CreateTransaction")
            .WithSummary("Create a new financial transaction")
            .Produces<TransactionResponse>(202)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

        group.MapGet("/{id}", GetTransactionByIdAsync)
            .WithName("GetTransactionById")
            .WithSummary("Get a transaction by ID")
            .Produces<TransactionResponse>()
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(404)
            .Produces<ErrorResponse>(500);

        group.MapGet("/", ListTransactionsAsync)
            .WithName("ListTransactions")
            .WithSummary("List transactions with pagination")
            .Produces<TransactionResponse>()
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);
    }

    private static async Task<IResult> CreateTransactionAsync(CreateTransactionRequest request, IMediator mediator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(request.ToCommand(context), cancellationToken);
        return ToHttpResult(response);
    }

    private static async Task<IResult> GetTransactionByIdAsync(
        string id,
        IMediator mediator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var tracerId = context.TraceIdentifier;
        var userId = context.User.FindFirst("sub")?.Value;
        var query = new GetTransactionByIdQuery(tracerId, id, userId);
        var response = await mediator.Send(query, cancellationToken);
        return ToHttpResult(response);
    }

    private static async Task<IResult> ListTransactionsAsync(
        IMediator mediator,
        HttpContext context,
        CancellationToken cancellationToken,
        DateTime? startDate = null,
        DateTime? endDate = null,
        TransactionType? type = null,
        int page = 1,
        int pageSize = 20)
    {
        var tracerId = context.TraceIdentifier;
        var userId = context.User.FindFirst("sub")?.Value;

        var query = new ListTransactionsQuery(
            tracerId,
            userId,
            startDate ?? DateTime.UtcNow.Date.AddDays(-30),
            endDate ?? DateTime.UtcNow.Date,
            type,
            page,
            pageSize);

        var response = await mediator.Send(query, cancellationToken);
        return ToHttpResult(response);
    }

    private static IResult ToHttpResult(Response response) =>
        Results.Json(
            response.IsSuccess ? response.Data : response.ErrorContent.ErrorResponse,
            statusCode: response.StatusCode);
}
