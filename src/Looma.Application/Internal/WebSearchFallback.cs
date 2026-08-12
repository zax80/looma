using Looma.Application.Configuration;
using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.Internal;

/// <summary>
/// The "local retrieval found nothing, try the web instead" step shared by
/// <see cref="Looma.Application.UseCases.AnswerUseCase"/> and
/// <see cref="Looma.Application.UseCases.ChatCompletionUseCase"/> — same
/// extraction reasoning as <see cref="RagRetrieval"/>: one place decides
/// how a web result becomes a citation, so the two use cases can't
/// silently drift apart on it. Deliberately a SEPARATE helper from
/// <see cref="RagRetrieval"/> rather than folded into it — AnswerUseCase's
/// own retrieval call doesn't go through <see cref="RagRetrieval"/> at all
/// (see that class's doc comment), so a shared step both use cases can call
/// AFTER their own already-verified retrieval, without either one having to
/// adopt the other's retrieval shape, is the lower-risk seam.
///
/// The trigger is deterministic — zero local citations, not a model
/// decision — see <see cref="RagOptions.EnableWebSearch"/>'s doc comment
/// for why.
/// </summary>
internal static class WebSearchFallback
{
    /// <summary>
    /// Returns <paramref name="localCitations"/> unchanged whenever it's
    /// non-empty, web search is disabled, or the web search itself finds
    /// nothing/fails (see <see cref="IWebSearchProvider"/>'s fail-closed
    /// contract — this method never throws on the caller's behalf for a
    /// web search failure, only for a genuine cancellation).
    /// </summary>
    public static async Task<List<DocumentChunk>> AugmentIfEmptyAsync(
        List<DocumentChunk> localCitations,
        string query,
        RagOptions ragOptions,
        IWebSearchProvider webSearchProvider,
        CancellationToken cancellationToken)
    {
        if (localCitations.Count > 0 || !ragOptions.EnableWebSearch)
        {
            return localCitations;
        }

        var results = await webSearchProvider
            .SearchAsync(query, ragOptions.WebSearchMaxResults, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            return localCitations;
        }

        var indexedAt = DateTimeOffset.UtcNow;
        return results
            .Select((result, index) => new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                SourceId = result.Url,
                Content = string.IsNullOrWhiteSpace(result.Snippet)
                    ? result.Title
                    : $"{result.Title}\n{result.Snippet}",
                Metadata = new ChunkMetadata
                {
                    SourcePath = result.Url,
                    MediaType = MediaType.Web,
                    ChunkIndex = index,
                    IndexedAtUtc = indexedAt
                }
            })
            .ToList();
    }
}
