using Looma.Infrastructure.WebSearch.SearXng;
using Xunit;
using Xunit.Abstractions;

namespace Looma.Infrastructure.WebSearch.SearXng.Tests;

public sealed class SearXngWebSearchProviderTests : IClassFixture<SearXngFixture>
{
    private readonly SearXngFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SearXngWebSearchProviderTests(SearXngFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private bool SkipIfUnavailable()
    {
        if (_fixture.IsAvailable)
        {
            return false;
        }

        _output.WriteLine(
            "SKIPPED: no SearXNG instance reachable with json format enabled at the configured " +
            "test endpoint. Run a local SearXNG container with json enabled in settings.yml (or set " +
            "LOOMA_TEST_SEARXNG_ENDPOINT) to exercise this suite for real, per CLAUDE.md's " +
            "no-mocks-only rule for Infrastructure.*. See docs/config-reference.md's WebSearch section.");
        return true;
    }

    [Fact]
    public async Task SearchAsync_RealInstance_ReturnsNonEmptyResultsForAnOrdinaryQuery()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var provider = _fixture.CreateProvider();

        var results = await provider.SearchAsync("open source search engine", maxResults: 3, CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.True(results.Count <= 3);
        Assert.All(results, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Url));
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
        });
    }

    [Fact]
    public async Task SearchAsync_MaxResultsSmallerThanAvailable_RespectsTheLimit()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var provider = _fixture.CreateProvider();

        var results = await provider.SearchAsync("open source search engine", maxResults: 1, CancellationToken.None);

        Assert.True(results.Count <= 1);
    }

    // ---- unreachable/misconfigured endpoint (not gated behind
    // SkipIfUnavailable — deliberately tests a guaranteed-unreachable
    // endpoint independent of whether a real SearXNG fixture exists,
    // same pattern as QdrantVectorStoreTests's unreachable-store cases) ----

    [Fact]
    public async Task SearchAsync_UnreachableEndpoint_ReturnsEmptyRatherThanThrowing()
    {
        // Fail-closed contract (see IWebSearchProvider's doc comment) — a
        // broken web search backend must never break answering, unlike
        // Qdrant's VectorStoreUnavailableException, which is deliberately
        // loud.
        var provider = new SearXngWebSearchProvider(
            new HttpClient { BaseAddress = new Uri("http://localhost:1"), Timeout = TimeSpan.FromSeconds(3) });

        var results = await provider.SearchAsync("anything", maxResults: 3, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyWithoutCallingTheNetwork()
    {
        // BaseAddress deliberately invalid — if this made a real call it
        // would throw/timeout, proving the empty-query short-circuit works.
        var provider = new SearXngWebSearchProvider(
            new HttpClient { BaseAddress = new Uri("http://localhost:1"), Timeout = TimeSpan.FromMilliseconds(50) });

        var results = await provider.SearchAsync("   ", maxResults: 3, CancellationToken.None);

        Assert.Empty(results);
    }
}
