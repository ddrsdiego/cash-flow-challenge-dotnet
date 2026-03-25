namespace CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;

using System;
using CashFlow.SharedKernel.Application.Utils;
using MediatR;

public record GetDailyConsolidationQuery(
    string TracerId,
    string UserId,
    DateTime Date) : IRequest<Response>;
