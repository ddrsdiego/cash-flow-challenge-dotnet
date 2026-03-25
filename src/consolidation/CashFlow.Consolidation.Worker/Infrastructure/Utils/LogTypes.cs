namespace CashFlow.Consolidation.Worker.Infrastructure.Utils;

/// <summary>
/// Centralized log type identifiers for consistent filtering across logs.
/// Use these constants in LoggerMessage definitions.
/// </summary>
public static class LogTypes
{
    // Consumers
    public const string TransactionCreatedConsumer = "transaction-created-consumer";
    public const string ConsolidationBatchReceivedConsumer = "consolidation-batch-received-consumer";

    // Use Cases / Handlers
    public const string IngestTransactionsBatch = "ingest-transactions-batch";
    public const string ProcessConsolidationBatch = "process-consolidation-batch";
}
