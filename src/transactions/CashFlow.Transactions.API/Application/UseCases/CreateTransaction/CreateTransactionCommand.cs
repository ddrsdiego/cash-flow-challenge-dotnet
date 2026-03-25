using System;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Enums;
using MediatR;

namespace CashFlow.Transactions.API.Application.UseCases.CreateTransaction;

public record CreateTransactionCommand(
    string TracerId,
    string IdempotencyKey,
    string UserId,
    TransactionType Type,
    decimal Amount,
    string Description,
    Category Category,
    DateTime Date) : IRequest<Response>;
