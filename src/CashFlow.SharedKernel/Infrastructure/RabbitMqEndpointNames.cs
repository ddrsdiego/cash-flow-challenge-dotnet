namespace CashFlow.SharedKernel.Infrastructure;

/// <summary>
/// Centralized RabbitMQ endpoint configuration.
/// - Queue:     consolidation.process
/// - Exchange:  cashflow.transactions (fanout — broadcasts to all bound queues)
/// </summary>
public static class RabbitMqEndpointNames
{
    public static readonly RabbitMqEndpoint TransactionCreated = new(
        QueueName: "consolidation.process",
        Exchange: "cashflow.transactions");
}

/// <summary>
/// Represents a RabbitMQ endpoint configuration.
/// </summary>
/// <param name="QueueName">The RabbitMQ queue name</param>
/// <param name="Exchange">The RabbitMQ exchange name</param>
public record RabbitMqEndpoint(string QueueName, string Exchange);
