namespace CashFlow.SharedKernel.Infrastructure;

/// <summary>
/// Centralized RabbitMQ endpoint configuration.
/// 
/// **transaction.batch.process** (fanout):
///   - Published by: Transactions Worker Batcher
///   - Queue: transaction.batch.process
///   - Consumed by: Transactions Worker Processor (TransactionBatchReadyConsumer)
///
/// **cashflow.transactions** (fanout):
///   - Published by: Transactions Worker Processor
///   - Queue: consolidation.process
///   - Consumed by: Consolidation Worker (TransactionCreatedConsumer)
///
/// **cashflow.consolidation** (fanout):
///   - Published by: Consolidation Worker
///   - Consumed by: Consolidation Worker (ConsolidationBatchReceivedConsumer) and Consolidation API (DailyConsolidationUpdatedConsumer)
/// </summary>
public static class RabbitMqEndpointNames
{
    public static readonly RabbitMqEndpoint TransactionBatchReady = new(
        QueueName: "transaction.batch.process",
        Exchange: "transaction.batch.process");

    public static readonly RabbitMqEndpoint TransactionCreated = new(
        QueueName: "consolidation.process",
        Exchange: "cashflow.transactions");

    public static readonly RabbitMqEndpoint DailyConsolidationUpdated = new(
        QueueName: "consolidation.updated",
        Exchange: "cashflow.consolidation");

    public static readonly RabbitMqEndpoint DailyConsolidationUpdatedCache = new(
        QueueName: "consolidation.api.cache",
        Exchange: "cashflow.consolidation");
}

/// <summary>
/// Represents a RabbitMQ endpoint configuration.
/// </summary>
/// <param name="QueueName">The RabbitMQ queue name</param>
/// <param name="Exchange">The RabbitMQ exchange name</param>
public record RabbitMqEndpoint(string QueueName, string Exchange);
