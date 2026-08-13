namespace Looma.Infrastructure.WebSearch.DuckDuckGo;

/// <summary>
/// Binds to the <c>WebSearch</c> section of config.json — connection
/// details only (see <c>RagOptions.EnableWebSearch</c>/<c>WebSearchMaxResults</c>
/// in Looma.Application for the "should we use it, how many results" side
/// of this).
///
/// Always registered regardless of whether <c>RAG.EnableWebSearch</c> is
/// on — <c>DuckDuckGoWebSearchProvider</c> fails closed to an empty result
/// list rather than throwing, so this being unreachable/blocked is
/// harmless as long as that flag stays false (the documented default).
/// </summary>
public sealed class DuckDuckGoOptions
{
    public const string SectionName = "WebSearch";

    /// <summary>
    /// DuckDuckGo's own HTML results endpoint — no account, no API key,
    /// nothing to configure for a typical setup. Overridable in case
    /// DuckDuckGo ever blocks requests from your IP/network and you want
    /// to point this at a mirror or proxy — see
    /// docs/config-reference.md's WebSearch section for the scraping
    /// caveats this endpoint carries (unofficial, unversioned, can break).
    /// </summary>
    public string Endpoint { get; set; } = "https://html.duckduckgo.com";

    public int TimeoutSeconds { get; set; } = 10;
}
