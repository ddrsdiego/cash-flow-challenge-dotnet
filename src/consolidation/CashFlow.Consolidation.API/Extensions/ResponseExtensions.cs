namespace CashFlow.Consolidation.API.Extensions;

using System;
using System.Linq;
using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public static class ResponseExtensions
{
    /// <summary>
    /// Logs and validates response status code.
    /// - 4xx: Logs as Warning, returns silently (no retry)
    /// - 5xx: Throws exception for MassTransit retry handling
    /// </summary>
    public static void ThrowIfServerError(this Response response, ILogger logger, string logType, string tracerId)
    {
        if (!response.IsFailure)
            return;

        logger.LogWarning("[{LogType}] | [{TracerId}] - Failed to process: {@ErrorResponse}",
            logType,
            tracerId,
            response.ErrorContent?.ErrorResponse);

        if (response.StatusCode == StatusCodes.Status500InternalServerError)
            throw new InvalidOperationException(response.ToErrorMessage());
    }

    private static string ToErrorMessage(this Response response) =>
        string.Join(" - ", response.ErrorContent?.ErrorResponse?.Errors?.Select(x => x.Error) ?? Array.Empty<string>());
}
