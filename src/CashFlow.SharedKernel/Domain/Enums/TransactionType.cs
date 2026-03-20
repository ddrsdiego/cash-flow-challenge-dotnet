namespace CashFlow.SharedKernel.Domain.Enums;

/// <summary>
/// Represents the type of a financial transaction.
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// A debit transaction — money leaving the account (expense).
    /// </summary>
    Debit,

    /// <summary>
    /// A credit transaction — money entering the account (revenue).
    /// </summary>
    Credit
}
