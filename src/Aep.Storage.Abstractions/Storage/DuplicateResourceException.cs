namespace Aep.Storage.Abstractions.Storage;

/// <summary>
/// Thrown by <see cref="IResourceStore.InsertAsync"/> when a resource with the
/// same path already exists. The host maps this to HTTP 409 Conflict.
/// </summary>
public sealed class DuplicateResourceException(string path)
    : Exception($"resource \"{path}\" already exists")
{
    public string Path { get; } = path;
}
