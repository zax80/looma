using Looma.Core.Entities;

namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

/// <summary>
/// Whether a cached answer that cites at least one web search result
/// (<see cref="MediaType.Web"/>) has outlived
/// <see cref="AnswerCacheOptions.WebCitationMaxAgeHours"/> — pure logic, no
/// I/O, shared by both the exact-match (<see cref="ExactCacheEntry"/>) and
/// semantic (<see cref="AnswerCachePayload"/>) layers since both carry the
/// same relevant fields (citations, created-at).
///
/// Answers with no web citation are NEVER treated as stale this way —
/// their own staleness is already handled by the existing
/// <c>DocumentsVersion</c> invalidation check (a re-index changes the
/// chunk count and invalidates every prior entry). This exists specifically
/// because a web citation's real-world content can change on its own, with
/// nothing in this system ever re-indexing it to notice — without this, a
/// web-sourced cached answer could serve stale information forever.
/// </summary>
internal static class AnswerCacheStaleness
{
    public static bool IsStale(
        IReadOnlyList<DocumentChunk> citations,
        DateTimeOffset createdAtUtc,
        int? webCitationMaxAgeHours,
        DateTimeOffset now)
    {
        if (webCitationMaxAgeHours is not { } maxAgeHours)
        {
            return false;
        }

        var hasWebCitation = citations.Any(c => c.Metadata.MediaType == MediaType.Web);
        if (!hasWebCitation)
        {
            return false;
        }

        return now - createdAtUtc > TimeSpan.FromHours(maxAgeHours);
    }
}
