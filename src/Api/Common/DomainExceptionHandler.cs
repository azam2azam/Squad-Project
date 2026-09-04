using Application.Abstractions;
using Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.Common;

/// <summary>
/// Translates domain invariant violations into RFC 7807 responses so callers get a
/// 400 with a readable reason instead of a 500 (spec section 7).
/// </summary>
public sealed class DomainExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext,
        Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            WorkbookFormatException => (StatusCodes.Status400BadRequest, "That spreadsheet could not be read"),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Not signed in"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Not permitted"),
            DomainException => (StatusCodes.Status400BadRequest, "Request violates a domain rule"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            // A missing rendering engine is a deployment gap, not a server fault: 503
            // says "not here", and the message tells the operator how to fix it.
            ExportUnavailableException => (StatusCodes.Status503ServiceUnavailable,
                "Export is unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        // Field-level failures go in the standard "errors" extension so the client can
        // attach each message to the input that produced it.
        var extensions = new Dictionary<string, object?>();
        if (exception is ValidationException validationException)
        {
            extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Path}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Internal errors must not leak exception text to the caller.
                Detail = status == StatusCodes.Status500InternalServerError
                    ? null
                    : exception.Message,
                Instance = httpContext.Request.Path,
                Extensions = extensions
            }
        });
    }
}
