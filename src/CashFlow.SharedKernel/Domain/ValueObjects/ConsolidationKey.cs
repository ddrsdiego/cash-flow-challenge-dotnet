using System;
using System.Globalization;

namespace CashFlow.SharedKernel.Domain.ValueObjects;

/// <summary>
/// Value Object representing the composite key for DailyConsolidation.
/// Immutable and uses value-based equality for use as Dictionary key.
/// Format: {UserId-sem-hifens}_{Date:yyyyMMdd} (e.g., "f0a00b110b2f4495913df00df3b12a65_20260320")
/// </summary>
public readonly record struct ConsolidationKey(string UserId, DateTime Date)
{
    /// <summary>
    /// Generates the composite key string in format: {UserId-sem-hifens}_{Date:yyyyMMdd}
    /// UUID hyphens are removed to avoid ambiguity when parsing.
    /// </summary>
    public string Value => $"{UserId.Replace("-", "")}_{Date:yyyyMMdd}";

    /// <summary>
    /// Attempts to parse a key string back into UserId and Date components.
    /// Handles the format: {userId-sem-hifens}_{yyyyMMdd}
    /// </summary>
    public static bool TryParse(string value, out ConsolidationKey key)
    {
        key = default;
        
        if (string.IsNullOrEmpty(value)) 
            return false;

        var separatorIndex = value.IndexOf('_');
        if (separatorIndex < 0) 
            return false;

        var userId = value[..separatorIndex];
        var dateStr = value[(separatorIndex + 1)..];

        if (!DateTime.TryParseExact(dateStr, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        key = new ConsolidationKey(userId, date);
        return true;
    }
}
