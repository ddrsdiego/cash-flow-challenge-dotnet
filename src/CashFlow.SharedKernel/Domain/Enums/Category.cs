namespace CashFlow.SharedKernel.Domain.Enums;

/// <summary>
/// Represents the category of a financial transaction.
/// </summary>
public enum Category
{
    /// <summary>
    /// Revenue from sales of goods or products.
    /// </summary>
    Sales,

    /// <summary>
    /// Revenue from services rendered.
    /// </summary>
    Services,

    /// <summary>
    /// Expenses for purchasing supplies or raw materials.
    /// </summary>
    Supplies,

    /// <summary>
    /// Expenses for utilities such as electricity, water, and internet.
    /// </summary>
    Utilities,

    /// <summary>
    /// Returns or refunds of previously purchased goods or services.
    /// </summary>
    Returns,

    /// <summary>
    /// Any other type of transaction not covered by the above categories.
    /// </summary>
    Other
}
