using System.Collections.Generic;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Messages;
using MediatR;

namespace CashFlow.Consolidation.Worker.Application.UseCases.IngestTransactionsBatch;

public record IngestTransactionsBatchCommand(
    string TracerId,
    string BatchId,
    IReadOnlyList<TransactionItem> Transactions) : IRequest<Response>;
