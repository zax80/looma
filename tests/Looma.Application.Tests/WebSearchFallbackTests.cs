using Looma.Application.Configuration;
using Looma.Application.Internal;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Xunit;

namespace Looma.Application.Tests;

public sealed class WebSearchFallbackTests
{
    [Fact]
    public async Task TryWebSearchAsync_WebSearchDisabled_ReturnsEmptyAndNeverCallsProvider()
    {
        var provider = new FakeWebSearchProvider(shouldNotBeCalled: true);

        var result = await WebSearchFallback.TryWebSearchAsync(
            "query", new RagOptions { EnableWebSearch = false }, provider, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task TryWebSearchAsync_Enabled_MapsWebResultsToDocumentChunksWithMediaTypeWeb()
    {
        var provider = new FakeWebSearchProvider(results:
        [
            new WebSearchResult { Title = "Coffee Brewing Guide", Url = "https://example.com/coffee", Snippet = "How to brew coffee." }
        ]);

        var result = await WebSearchFallback.TryWebSearchAsync(
            "how to brew coffee", new RagOptions { EnableWebSearch = true, WebSearchMaxResults = 3 }, provider, CancellationToken.None);

        var chunk = Assert.Single(result);
        Assert.Equal("https://example.com/coffee", chunk.SourceId);
        Assert.Equal(MediaType.Web, chunk.Metadata.MediaType);
        Assert.Equal("https://example.com/coffee", chunk.Metadata.SourcePath);
        Assert.Contains("Coffee Brewing Guide", chunk.Content);
        Assert.Contains("How to brew coffee.", chunk.Content);
        Assert.Equal("how to brew coffee", provider.LastQuery);
        Assert.Equal(3, provider.LastMaxResults);
    }

    [Fact]
    public async Task TryWebSearchAsync_ProviderReturnsNoResults_ReturnsEmpty()
    {
        var provider = new FakeWebSearchProvider(results: []);

        var result = await WebSearchFallback.TryWebSearchAsync(
            "query", new RagOptions { EnableWebSearch = true }, provider, CancellationToken.None);

        Assert.Empty(result);
    }

    private sealed class FakeWebSearchProvider : IWebSearchProvider
    {
        private readonly IReadOnlyList<WebSearchResult> _results;
        private readonly bool _shouldNotBeCalled;

        public string? LastQuery { get; private set; }
        public int? LastMaxResults { get; private set; }

        public FakeWebSearchProvider(IReadOnlyList<WebSearchResult>? results = null, bool shouldNotBeCalled = false)
        {
            _results = results ?? [];
            _shouldNotBeCalled = shouldNotBeCalled;
        }

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            if (_shouldNotBeCalled)
            {
                throw new InvalidOperationException("IWebSearchProvider.SearchAsync should not have been called here.");
            }

            LastQuery = query;
            LastMaxResults = maxResults;
            return Task.FromResult(_results);
        }
    }
}
