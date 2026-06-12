using Aep.Server.Http;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;

namespace Aep.Storage.Benchmarks;

/// <summary>
/// Cost of the opaque page-token codec (AES-GCM encrypt/decrypt per List page). Pagination does
/// one Protect per response and one Unprotect per continuation, so this is the per-page overhead.
/// </summary>
[MemoryDiagnoser]
public class PageTokenBenchmarks
{
    private PageTokenProtector _protector = null!;
    private string _token = null!;

    [GlobalSetup]
    public void Setup()
    {
        _protector = new PageTokenProtector(Options.Create(new PageTokenOptions()));
        _token = _protector.Protect("book", "publishers/p1/books/b500");
    }

    [Benchmark]
    public string Protect() => _protector.Protect("book", "publishers/p1/books/b500");

    [Benchmark]
    public string? Unprotect() => _protector.Unprotect("book", _token);
}
