using System;
using System.Linq;
using CashFlow.SharedKernel.Application.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CashFlow.Transactions.Worker.Extensions;

public static class ResponseExtensions
{
    /// <summary>
    /// Evaluates a Response and throws an exception if it's a server error (5xx).
    /// For 4xx errors, logs a warning and returns normally (message is ACK'd).
    /// </summary>
    public static void ThrowIfServerError(this Response response, ILogger logger, string logType, string tracerId)
    {
        if (!response.IsFailure)
            return;

        logger.LogWarning("[{LogType}] | [{TracerId}] - Failed to process: {@ErrorResponse}",
            logType,
            tracerId,
            response.ErrorContent.ErrorResponse);

        if (response.StatusCode == StatusCodes.Status500InternalServerError)
            throw new InvalidOperationException(response.ToErrorMessage());
    }

    private static string ToErrorMessage(this Response response) =>
        string.Join(" - ", response.ErrorContent.ErrorResponse.Errors.Select(x => x.Error));
}
