using Aep.Storage.Abstractions.Filtering;

namespace Aep.Storage.Abstractions.Model;

/// <summary>Options controlling a List call (AEP-158 pagination, AEP-160 filtering).</summary>
public sealed class ListOptions
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 1000;

    /// <summary>Effective page size, clamped to [1, <see cref="MaxPageSize"/>].</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Opaque continuation token from a previous response.</summary>
    public string? PageToken { get; init; }

    /// <summary>Number of results to skip before returning (AEP-158 skip).</summary>
    public int Skip { get; init; }

    /// <summary>Parsed filter expression, or null when no filter was supplied.</summary>
    public FilterExpression? Filter { get; init; }
}

/// <summary>The result of a List call.</summary>
public sealed class ListResult
{
    public required IReadOnlyList<StoredResource> Items { get; init; }

    /// <summary>Token to fetch the next page, or null/empty when there are no more results.</summary>
    public string? NextPageToken { get; init; }
}
