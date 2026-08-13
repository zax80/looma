using Looma.Application.Configuration;
using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.Internal;

/// <summary>
/// The "ask the web instead" step shared by
/// <see cref="Looma.Application.UseCases.AnswerUseCase"/> and
/// <see cref="Looma.Application.UseCases.ChatCompletionUseCase"/> — same
/// extraction reasoning as <see cref="RagRetrieval"/>: one place decides
/// how a web result becomes a citation, so the two use cases can't
/// silently drift apart on it.
///
/// Called by the use case AFTER a first, local-only generation attempt
/// comes back as a refusal (<see cref="GroundedAnswer.NoAnswerSentence"/>)
/// — NOT gated on local retrieval returning zero citations, which is what
/// this originally did and was a real bug caught in live testing: local
/// retrieval (especially with <see cref="RagOptions.EnableAdaptiveThreshold"/>
/// on) routinely returns SOME citations for almost any query — a handful
/// of weakly-scoring, ultimately irrelevant chunks is the common case, not
/// the exception — so "zero citations" essentially never happened, and
/// this fallback almost never fired even when it should have. Asking
/// "did the model actually answer, or refuse?" is the correct signal for
/// "was anything usable found," not "was the citations list non-empty."
/// </summary>
internal static class WebSearchFallback
{
    /// <summary>
    /// Returns an empty list whenever web search is disabled or the
    /// search itself finds nothing/fails (see <see cref="IWebSearchProvider"/>'s
    /// fail-closed contract — this method never throws on the caller's
    /// behalf for a web search failure, only for a genuine cancellation).
    /// The caller decides what to do with an empty result (typically: keep
    /// the original refusal).
    /// </summary>
    public static async Task<List<DocumentChunk>> TryWebSearchAsync(
        string query,
        RagOptions ragOptions,
        IWebSearchProvider webSearchProvider,
        CancellationToken cancellationToken)
    {
        if (!ragOptions.EnableWebSearch)
        {
            return [];
        }

        var results = await webSearchProvider
            .SearchAsync(query, ragOptions.WebSearchMaxResults, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            return [];
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
