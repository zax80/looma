using Looma.Infrastructure.WebSearch.DuckDuckGo.Internal;
using Xunit;

namespace Looma.Infrastructure.WebSearch.DuckDuckGo.Tests;

/// <summary>
/// Covers <see cref="DuckDuckGoResultParser"/> against a canned HTML
/// sample built from DuckDuckGo's long-documented, years-stable
/// <c>result__a</c>/<c>result__snippet</c> markup — see the parser's own
/// doc comment for why this couldn't be verified against a real live
/// response, and why that matters if this ever needs revisiting.
/// Deliberately entirely offline: no network call, no fixture skip logic
/// needed, unlike <see cref="DuckDuckGoWebSearchProviderTests"/>'s live
/// smoke test.
/// </summary>
public sealed class DuckDuckGoResultParserTests
{
    // Two ordinary results (DuckDuckGo's uddg-wrapped redirect links,
    // bold tags around matched terms, HTML entities) plus a third with a
    // direct (non-wrapped) href, covering the fallback path.
    private const string SampleHtml = """
        <div class="results">
          <div class="result results_links results_links_deep web-result">
            <div class="links_main links_deep result__body">
              <h2 class="result__title">
                <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FCoffee&amp;rut=abc123">
                  <b>Coffee</b> - Wikipedia
                </a>
              </h2>
              <a class="result__snippet" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fen.wikipedia.org%2Fwiki%2FCoffee&amp;rut=abc123">
                <b>Coffee</b> is a drink prepared from roasted coffee beans &amp; hot water.
              </a>
            </div>
          </div>
          <div class="result results_links results_links_deep web-result">
            <div class="links_main links_deep result__body">
              <h2 class="result__title">
                <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.ncausa.org%2Fabout-coffee&amp;rut=def456">
                  About <b>Coffee</b> | National <b>Coffee</b> Association
                </a>
              </h2>
              <a class="result__snippet" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.ncausa.org%2Fabout-coffee&amp;rut=def456">
                Learn about the history and brewing of <b>coffee</b>.
              </a>
            </div>
          </div>
          <div class="result results_links results_links_deep web-result">
            <div class="links_main links_deep result__body">
              <h2 class="result__title">
                <a rel="nofollow" class="result__a" href="https://direct-example.com/page">Direct Link Result</a>
              </h2>
              <a class="result__snippet" href="https://direct-example.com/page">A result linked directly, no uddg wrapper.</a>
            </div>
          </div>
        </div>
        """;

    [Fact]
    public void Parse_SampleHtml_ExtractsAllThreeResultsInOrder()
    {
        var results = DuckDuckGoResultParser.Parse(SampleHtml, maxResults: 10);

        Assert.Equal(3, results.Count);

        Assert.Equal("Coffee - Wikipedia", results[0].Title);
        Assert.Equal("https://en.wikipedia.org/wiki/Coffee", results[0].Url);
        Assert.Equal("Coffee is a drink prepared from roasted coffee beans & hot water.", results[0].Snippet);

        Assert.Equal("About Coffee | National Coffee Association", results[1].Title);
        Assert.Equal("https://www.ncausa.org/about-coffee", results[1].Url);
    }

    [Fact]
    public void Parse_DirectHrefWithoutUddgWrapper_FallsBackToTheRawUrl()
    {
        var results = DuckDuckGoResultParser.Parse(SampleHtml, maxResults: 10);

        var direct = results[2];
        Assert.Equal("Direct Link Result", direct.Title);
        Assert.Equal("https://direct-example.com/page", direct.Url);
        Assert.Equal("A result linked directly, no uddg wrapper.", direct.Snippet);
    }

    [Fact]
    public void Parse_MaxResultsSmallerThanAvailable_StopsAtTheLimit()
    {
        var results = DuckDuckGoResultParser.Parse(SampleHtml, maxResults: 1);

        Assert.Single(results);
        Assert.Equal("Coffee - Wikipedia", results[0].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_EmptyOrNullHtml_ReturnsEmpty(string? html)
    {
        var results = DuckDuckGoResultParser.Parse(html!, maxResults: 10);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_ZeroMaxResults_ReturnsEmpty()
    {
        var results = DuckDuckGoResultParser.Parse(SampleHtml, maxResults: 0);

        Assert.Empty(results);
    }

    [Fact]
    public void Parse_NoMatchingMarkup_ReturnsEmptyRatherThanThrowing()
    {
        // Simulates the "DuckDuckGo changed its markup" failure mode this
        // parser's doc comment warns about — must degrade gracefully, not
        // throw, per IWebSearchProvider's fail-closed contract.
        var results = DuckDuckGoResultParser.Parse("<html><body>Unexpected page structure</body></html>", maxResults: 10);

        Assert.Empty(results);
    }
}
