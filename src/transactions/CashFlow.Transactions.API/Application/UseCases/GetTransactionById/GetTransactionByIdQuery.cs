using CashFlow.SharedKernel.Application.Utils;
using MediatR;

namespace CashFlow.Transactions.API.Application.UseCases.GetTransactionById;

public record GetTransactionByIdQuery(
    string TracerId,
    string TransactionId,
    string? UserId) : IRequest<Response>;
