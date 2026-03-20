using System.Collections.Generic;

namespace CashFlow.SharedKernel.Application.Utils;

public class Response
{
    public int StatusCode { get; init; }
    public bool IsSuccess { get; init; }
    public bool IsFailure => !IsSuccess;
    public object Data { get; init; }
    public ErrorContent ErrorContent { get; init; }

    public static Response Ok(object data = null) =>
        new()
        {
            StatusCode = 200,
            IsSuccess = true,
            Data = data
        };

    public static Response Created(object data = null) =>
        new()
        {
            StatusCode = 201,
            IsSuccess = true,
            Data = data
        };

    public static Response BadRequest(ErrorResponse errorResponse) =>
        new()
        {
            StatusCode = 400,
            IsSuccess = false,
            ErrorContent = new ErrorContent { ErrorResponse = errorResponse }
        };

    public static Response NotFound(ErrorResponse errorResponse) =>
        new()
        {
            StatusCode = 404,
            IsSuccess = false,
            ErrorContent = new ErrorContent { ErrorResponse = errorResponse }
        };

    public static Response InternalServerError(ErrorResponse errorResponse) =>
        new()
        {
            StatusCode = 500,
            IsSuccess = false,
            ErrorContent = new ErrorContent { ErrorResponse = errorResponse }
        };

    public static ResponseBuilder Builder() => new();
}

public class ErrorContent
{
    public ErrorResponse ErrorResponse { get; set; }
}

public class ErrorResponse
{
    public string Instance { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public List<ErrorDetail> Errors { get; init; } = new List<ErrorDetail>();

    public static ErrorResponseBuilder Builder() => new();
}

public class ErrorDetail
{
    public string Code { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public class ResponseBuilder
{
    private int _statusCode = 200;
    private string _requestId;
    private ErrorResponse _errorResponse;
    private object _data;

    public ResponseBuilder WithStatusCode(int statusCode)
    {
        _statusCode = statusCode;
        return this;
    }

    public ResponseBuilder WithRequestId(string requestId)
    {
        _requestId = requestId;
        return this;
    }

    public ResponseBuilder WithData(object data)
    {
        _data = data;
        return this;
    }

    public ResponseBuilder WithErrorResponse(ErrorResponse errorResponse)
    {
        _errorResponse = errorResponse;
        return this;
    }

    public Response Build() =>
        new()
        {
            StatusCode = _statusCode,
            IsSuccess = _statusCode >= 200 && _statusCode < 300,
            Data = _data,
            ErrorContent = _errorResponse != null ? new ErrorContent { ErrorResponse = _errorResponse } : null
        };
}

public class ErrorResponseBuilder
{
    private string _instance;
    private string _traceId;
    private readonly List<ErrorDetail> _errors = new List<ErrorDetail>();

    public ErrorResponseBuilder WithInstance(string instance)
    {
        _instance = instance;
        return this;
    }

    public ErrorResponseBuilder WithTraceId(string traceId)
    {
        _traceId = traceId;
        return this;
    }

    public ErrorResponseBuilder WithError(string code, string error, string message)
    {
        _errors.Add(new ErrorDetail { Code = code, Error = error, Message = message });
        return this;
    }

    public ErrorResponse Build() =>
        new()
        {
            Instance = _instance,
            TraceId = _traceId,
            Errors = _errors
        };
}
