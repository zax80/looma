using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Looma.Infrastructure.WebSearch.DuckDuckGo.Internal;

namespace Looma.Infrastructure.WebSearch.DuckDuckGo;

/// <summary>
/// The one <see cref="IWebSearchProvider"/> implementation, hitting
/// DuckDuckGo's own HTML results page directly — no API key, no account,
/// no separate service to run. This is deliberately NOT DuckDuckGo's
/// official "Instant Answer" API (api.duckduckgo.com): that only returns
/// definition/disambiguation-style answer boxes for a narrow set of
/// queries, not ranked web results for arbitrary questions, so it can't
/// serve as a general web-search fallback. DuckDuckGo has no official
/// public web-search API at all — every "free DuckDuckGo search" tool,
/// this one included, works by fetching and parsing the same HTML page a
/// browser would get (see <see cref="Internal.DuckDuckGoResultParser"/>'s
/// doc comment for the parsing side of that and its real caveats).
///
/// Same fail-closed, best-effort contract as
/// <see cref="IWebSearchProvider"/> requires — this is a fallback for when
/// local retrieval already came up empty, so a blocked/rate-limited/
/// markup-changed response should degrade to "no web results" rather than
/// break the turn.
/// </summary>
public sealed class DuckDuckGoWebSearchProvider : IWebSearchProvider
{
    // DuckDuckGo's HTML endpoint is more likely to serve a normal result
    // page (rather than a bot-detection challenge) with a realistic
    // browser User-Agent than with .NET's default HttpClient identity.
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Safari/537.36";

    private readonly HttpClient _httpClient;

    public DuckDuckGoWebSearchProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
        {
            return [];
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/html/?q={Uri.EscapeDataString(query)}");
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Most likely a bot-detection block (DuckDuckGo may
                // throttle frequent/automated-looking requests) — no web
                // results this turn, not an error.
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return DuckDuckGoResultParser.Parse(html, maxResults);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a genuine cancellation — same fail-closed
            // treatment as any other connectivity failure below.
            return [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}
