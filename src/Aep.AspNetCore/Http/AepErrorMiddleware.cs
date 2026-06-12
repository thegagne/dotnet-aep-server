using Aep.Storage.Abstractions.Filtering;
using Aep.Storage.Abstractions.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Aep.Server.Http;

/// <summary>
/// Translates exceptions into AEP-193 error responses: RFC 9457 Problem Details
/// (<c>{ "type", "status", "title", "detail", "instance" }</c>) served as
/// <c>application/problem+json</c> via the framework's <see cref="IProblemDetailsService"/>.
/// Known domain exceptions map to their HTTP status; anything else becomes a 500.
/// </summary>
public sealed class AepErrorMiddleware(
    RequestDelegate next,
    IProblemDetailsService problemDetailsService,
    ILogger<AepErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AepException ex)
        {
            await WriteError(context, ex.StatusCode, ex.Message);
        }
        catch (DuplicateResourceException ex)
        {
            await WriteError(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (FilterParseException ex)
        {
            await WriteError(context, StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteError(context, StatusCodes.Status500InternalServerError, "internal server error");
        }
    }

    private async Task WriteError(HttpContext context, int status, string detail)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.Clear();
        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            // Type and Title are set explicitly so the framework's (partial) status-code
            // defaults don't apply. Type is a dereferenceable URI locating documentation
            // for the status; Title is the (non-occurrence-specific) HTTP reason phrase.
            Type = ProblemType(status),
            Title = ReasonPhrases.GetReasonPhrase(status) is { Length: > 0 } phrase ? phrase : "Error",
            Status = status,
            Detail = detail,
            Instance = context.Request.Path.HasValue ? context.Request.Path.Value : "/",
        };

        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
        });

        // Fall back to a direct write when no problem-details writer accepts the request
        // (e.g. an Accept header that excludes JSON). The shape is identical.
        if (!written)
        {
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    /// <summary>
    /// Maps an HTTP status to a dereferenceable URI used as the Problem Details <c>type</c>
    /// (a URI reference per RFC 9457). Points at the RFC section defining the status code,
    /// matching ASP.NET Core's own <c>ProblemDetailsDefaults</c> scheme. Falls back to
    /// <c>about:blank</c>.
    /// </summary>
    private static string ProblemType(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status412PreconditionFailed => "https://tools.ietf.org/html/rfc9110#section-15.5.13",
        StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc6585#section-4",
        StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        StatusCodes.Status501NotImplemented => "https://tools.ietf.org/html/rfc9110#section-15.6.2",
        StatusCodes.Status503ServiceUnavailable => "https://tools.ietf.org/html/rfc9110#section-15.6.4",
        StatusCodes.Status504GatewayTimeout => "https://tools.ietf.org/html/rfc9110#section-15.6.5",
        _ => "about:blank",
    };
}
