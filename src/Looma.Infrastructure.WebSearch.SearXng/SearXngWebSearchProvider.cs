using System.Net.Http.Json;
using System.Text.Json;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Looma.Infrastructure.WebSearch.SearXng.Internal;

namespace Looma.Infrastructure.WebSearch.SearXng;

/// <summary>
/// The one <see cref="IWebSearchProvider"/> implementation, talking to a
/// self-hosted SearXNG instance's plain HTTP JSON search API. See
/// <see cref="IWebSearchProvider"/>'s doc comment for why this is a
/// best-effort, fail-closed-to-empty call rather than one that throws on
/// failure like <c>QdrantVectorStore</c> — local retrieval already came up
/// empty by the time this runs (see <c>WebSearchFallback</c>), so a broken
/// or unconfigured web search backend should degrade to "no web results
/// either" rather than fail the whole answer.
/// </summary>
public sealed class SearXngWebSearchProvider : IWebSearchProvider
{
    private readonly HttpClient _httpClient;

    public SearXngWebSearchProvider(HttpClient httpClient)
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
            var requestUri = $"/search?q={Uri.EscapeDataString(query)}&format=json";
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Most likely cause: SearXNG's json format isn't enabled in
                // settings.yml (returns 403), or the instance isn't running
                // at all — either way, no web results this turn.
                return [];
            }

            var body = await response.Content
                .ReadFromJsonAsync<SearXngSearchResponse>(cancellationToken)
                .ConfigureAwait(false);

            return (body?.Results ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.Url))
                .Take(maxResults)
                .Select(r => new WebSearchResult
                {
                    Title = string.IsNullOrWhiteSpace(r.Title) ? r.Url! : r.Title!,
                    Url = r.Url!,
                    Snippet = r.Content ?? string.Empty
                })
                .ToList();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not a genuine cancellation — treat the same as any
            // other connectivity failure below: no web results, not an
            // error, per this class's fail-closed contract.
            return [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // Unreachable instance, or a non-JSON (HTML) response body that
            // failed to parse as SearXngSearchResponse — e.g. json format
            // genuinely isn't enabled but still returned 200 with an HTML
            // page. Either way: no web results this turn, not a failure.
            return [];
        }
    }
}
