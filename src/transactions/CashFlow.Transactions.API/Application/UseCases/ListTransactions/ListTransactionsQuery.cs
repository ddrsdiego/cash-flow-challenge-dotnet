using System;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Enums;
using MediatR;

namespace CashFlow.Transactions.API.Application.UseCases.ListTransactions;

public record ListTransactionsQuery(
    string TracerId,
    string? UserId,
    DateTime StartDate,
    DateTime EndDate,
    TransactionType? Type,
    int Page,
    int PageSize) : IRequest<Response>;
