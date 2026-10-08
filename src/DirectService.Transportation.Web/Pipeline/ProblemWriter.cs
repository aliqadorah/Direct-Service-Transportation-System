using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>Writes RFC 9457 problem details (API-06): type urn:dst:problem:&lt;code&gt;, message in the user's language, field errors.</summary>
internal static class ProblemWriter
{
    public static ProblemDetails Build(string code, int status, string language, string? field, object?[] args, IReadOnlyList<FieldError>? errors)
    {
        var problem = new ProblemDetails
        {
            Type = "urn:dst:problem:" + code,
            Title = code,
            Status = status,
            Detail = ErrorCatalog.Format(code, language, args),
        };
        problem.Extensions["code"] = code;
        if (field is not null) problem.Extensions["field"] = field;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors.Select(e => new
            {
                field = e.Field,
                code = e.Code,
                message = ErrorCatalog.Format(e.Code, language, e.Args),
            }).ToList();
        }
        return problem;
    }

    public static async Task WriteAsync(HttpContext http, string code, int status, string language, string? field = null,
        object?[]? args = null, IReadOnlyList<FieldError>? errors = null)
    {
        http.Response.StatusCode = status;
        var problem = Build(code, status, language, field, args ?? [], errors);
        problem.Extensions["traceId"] = http.TraceIdentifier;
        await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", http.RequestAborted)
            .ConfigureAwait(false);
    }
}

/// <summary>Maps exceptions to problem details; unexpected errors are logged and returned without internals.</summary>
internal sealed partial class AppExceptionHandler(ILogger<AppExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var language = httpContext.RequestServices.GetService<IRequestContext>()?.Language ?? "ar";
        switch (exception)
        {
            case AppException a:
                await ProblemWriter.WriteAsync(httpContext, a.Code, a.Status, language, a.Field, a.Args, a.Errors).ConfigureAwait(false);
                return true;
            case DbUpdateConcurrencyException:
                await ProblemWriter.WriteAsync(httpContext, ErrorCodes.ConcurrencyConflict, StatusCodes.Status409Conflict, language).ConfigureAwait(false);
                return true;
            case DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }:
                await ProblemWriter.WriteAsync(httpContext, ErrorCodes.DuplicateKey, StatusCodes.Status409Conflict, language, null, [""]).ConfigureAwait(false);
                return true;
            case DbUpdateException { InnerException: SqlException { Number: 547 } }:
                // A CHECK or FOREIGN KEY constraint refused the data: a validation gap, not a server fault (SR-03).
                LogUnexpected(log, exception, httpContext.TraceIdentifier);
                await ProblemWriter.WriteAsync(httpContext, ErrorCodes.ValidationFailed, StatusCodes.Status422UnprocessableEntity, language).ConfigureAwait(false);
                return true;
            case BadHttpRequestException:
                await ProblemWriter.WriteAsync(httpContext, ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest, language).ConfigureAwait(false);
                return true;
            default:
                LogUnexpected(log, exception, httpContext.TraceIdentifier);
                await ProblemWriter.WriteAsync(httpContext, ErrorCodes.ServerError, StatusCodes.Status500InternalServerError, language,
                    null, [httpContext.TraceIdentifier]).ConfigureAwait(false);
                return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled error (trace {TraceId}).")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string traceId);
}
