namespace Looma.Infrastructure.WebSearch.SearXng;

/// <summary>
/// Binds to the <c>WebSearch</c> section of config.json — connection
/// details only (see <c>RagOptions.EnableWebSearch</c>/<c>WebSearchMaxResults</c>
/// in Looma.Application for the "should we use it, how many results" side
/// of this, the same split as <c>QdrantOptions</c> (connection) vs
/// <c>RagOptions.TopK</c>/<c>MinRelevanceScore</c> (retrieval behavior)).
///
/// Always registered regardless of whether the user has actually enabled
/// or set up a SearXNG instance — <c>SearXngWebSearchProvider</c> fails
/// closed to an empty result list rather than throwing, so an unconfigured
/// or unreachable endpoint is harmless as long as <c>RAG.EnableWebSearch</c>
/// stays false (the documented default).
/// </summary>
public sealed class SearXngOptions
{
    public const string SectionName = "WebSearch";

    /// <summary>
    /// A self-hosted SearXNG instance with the <c>json</c> output format
    /// enabled in its <c>settings.yml</c> (disabled by default — see
    /// docs/config-reference.md's WebSearch section). Defaults to the
    /// conventional local SearXNG Docker port, same "assume localhost
    /// unless told otherwise" convention as <c>VectorStore.Endpoint</c>.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:8080";

    public int TimeoutSeconds { get; set; } = 10;
}
