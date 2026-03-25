using System.Collections.Generic;
using MediatR;
using CashFlow.SharedKernel.Application.Utils;

namespace CashFlow.Transactions.Worker.Application.UseCases.ProcessTransactionBatch;

/// <summary>
/// Command to process a batch of raw transaction requests.
/// Consumes TransactionBatchReadyEvent from RabbitMQ queue: transaction.batch.process.
/// Maps RawTransactionItem → Transaction, bulk inserts, marks requests as processed, publishes TransactionCreatedEvent.
/// </summary>
public record ProcessTransactionBatchCommand(
    string BatchId,
    string TracerId,
    IReadOnlyList<string> RawRequestIds) : IRequest<Response>;
