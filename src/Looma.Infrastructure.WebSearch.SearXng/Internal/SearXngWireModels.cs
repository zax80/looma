using System.Text.Json.Serialization;

namespace Looma.Infrastructure.WebSearch.SearXng.Internal;

/// <summary>
/// Shapes of SearXNG's <c>/search?format=json</c> response — confirmed
/// against SearXNG's own source (the <c>results</c> array's per-item
/// fields are <c>title</c>/<c>url</c>/<c>content</c>, not e.g. "snippet";
/// several third-party wrappers rename "content" to "snippet" in their OWN
/// output, which is a common point of confusion). Only the fields Looma
/// actually uses are modeled — SearXNG's real payload has many more
/// (engine, score, category, publishedDate, thumbnail, ...).
/// </summary>
internal sealed class SearXngSearchResponse
{
    [JsonPropertyName("results")]
    public List<SearXngResult>? Results { get; init; }
}

internal sealed class SearXngResult
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}
