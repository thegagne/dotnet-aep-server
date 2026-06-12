namespace Aep.Server.Http;

/// <summary>An error with an associated HTTP status, rendered as an AEP-193 error body.</summary>
public abstract class AepException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>The requested resource does not exist (HTTP 404).</summary>
public sealed class ResourceNotFoundException(string path)
    : AepException(StatusCodes.Status404NotFound, $"resource \"{path}\" not found");

/// <summary>The request body or parameters are invalid (HTTP 400).</summary>
public sealed class ResourceValidationException(string message)
    : AepException(StatusCodes.Status400BadRequest, message);

/// <summary>
/// A general-purpose AEP error with an explicit status, for use inside an interceptor or
/// backend decorator to abort an operation (e.g. <c>throw new AepStatusException(403, "forbidden")</c>).
/// </summary>
public sealed class AepStatusException(int statusCode, string message)
    : AepException(statusCode, message);
