using System.Text.RegularExpressions;
using Looma.Core.Entities;

namespace Looma.Infrastructure.WebSearch.DuckDuckGo.Internal;

/// <summary>
/// Extracts <see cref="WebSearchResult"/>s from DuckDuckGo's
/// <c>html.duckduckgo.com/html/</c> results page — regex against its
/// long-documented <c>result__a</c>/<c>result__snippet</c> markup, not a
/// real HTML parser (see <c>Looma.Infrastructure.WebSearch.DuckDuckGo.csproj</c>'s
/// doc comment for why). Kept as its own pure, dependency-free class
/// (no HttpClient, no I/O) specifically so it can be unit-tested against a
/// canned HTML sample without needing a live network call — see
/// DuckDuckGoResultParserTests.
///
/// IMPORTANT CAVEAT: this markup is unofficial and unversioned. It was
/// written against DuckDuckGo's long-stable, widely-documented structure
/// (used by numerous open-source scrapers for years), but could NOT be
/// verified against DuckDuckGo's actual live response from the sandbox
/// this was built in (no outbound network access to duckduckgo.com from
/// there). If <see cref="Parse"/> returns an empty list despite a
/// successful HTTP response, DuckDuckGo's markup has likely diverged from
/// what's coded here — capture a real response body and this needs fixing
/// against it, not guessed at again.
/// </summary>
internal static class DuckDuckGoResultParser
{
    private static readonly Regex TitleLinkPattern = new(
        """<a[^>]*class="result__a"[^>]*href="([^"]*)"[^>]*>(.*?)</a>""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SnippetPattern = new(
        """<a[^>]*class="result__snippet"[^>]*>(.*?)</a>""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagPattern = new("<.*?>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static List<WebSearchResult> Parse(string html, int maxResults)
    {
        if (string.IsNullOrEmpty(html) || maxResults <= 0)
        {
            return [];
        }

        var titleMatches = TitleLinkPattern.Matches(html);
        var snippetMatches = SnippetPattern.Matches(html);

        var results = new List<WebSearchResult>();
        for (var i = 0; i < titleMatches.Count && results.Count < maxResults; i++)
        {
            var url = ResolveTargetUrl(titleMatches[i].Groups[1].Value);
            var title = CleanText(titleMatches[i].Groups[2].Value);
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            // Snippets appear in the same order as titles in every sample
            // this was written against — a missing one here (index out of
            // range) just yields an empty snippet for that result, not a
            // dropped result.
            var snippet = i < snippetMatches.Count ? CleanText(snippetMatches[i].Groups[1].Value) : string.Empty;

            results.Add(new WebSearchResult { Title = title, Url = url, Snippet = snippet });
        }

        return results;
    }

    /// <summary>
    /// DuckDuckGo wraps every result link in its own redirect
    /// (<c>//duckduckgo.com/l/?uddg=&lt;url-encoded target&gt;&amp;rut=...</c>)
    /// instead of linking directly — this unwraps it back to the real
    /// target URL via the <c>uddg</c> query parameter. Falls back to the
    /// raw href (normalized to an absolute URL) if there's no
    /// <c>uddg</c> parameter, in case DuckDuckGo ever links some result
    /// types directly.
    /// </summary>
    private static string ResolveTargetUrl(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var decodedHref = System.Net.WebUtility.HtmlDecode(href);
        var normalized = decodedHref.StartsWith("//", StringComparison.Ordinal)
            ? "https:" + decodedHref
            : decodedHref;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var uddg = ExtractQueryParam(uri.Query, "uddg");
        return uddg ?? normalized;
    }

    private static string? ExtractQueryParam(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            if (string.Equals(pair[..separatorIndex], name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            }
        }

        return null;
    }

    private static string CleanText(string raw)
    {
        var withoutTags = TagPattern.Replace(raw, string.Empty);
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespacePattern.Replace(decoded, " ").Trim();
    }
}
