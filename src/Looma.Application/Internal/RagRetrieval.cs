using Looma.Application.Configuration;
using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.Internal;

/// <summary>
/// The "embed a query, search the documents collection, shape the results
/// into citations" step shared by <see cref="Looma.Application.UseCases.AnswerUseCase"/>
/// and <see cref="Looma.Application.UseCases.ChatUseCase"/>. Extracted here
/// so the two don't silently drift apart on exactly how a citation is
/// built — AnswerUseCase itself is left untouched (already verified,
/// no reason to risk it) and only ChatUseCase calls this.
///
/// Adaptive thresholding (see <see cref="RagOptions.EnableAdaptiveThreshold"/>)
/// also lives here rather than in AnswerUseCase, for the same reason —
/// this is the one call site allowed to diverge from AnswerUseCase's
/// already-verified flat-<see cref="RagOptions.MinRelevanceScore"/>
/// behavior.
/// </summary>
internal static class RagRetrieval
{
    public static async Task<List<DocumentChunk>> RetrieveCitationsAsync(
        IVectorStore vectorStore,
        ReadOnlyMemory<float> queryEmbedding,
        string queryText,
        RagOptions ragOptions,
        CancellationToken cancellationToken)
    {
        // With adaptive thresholding on, search against the lower
        // AdaptiveFloorScore so a genuinely relevant-but-lower-scoring
        // candidate is still fetched — ApplyAdaptiveThreshold below decides
        // whether it's actually kept. With it off, behave exactly as
        // before: search directly against the flat MinRelevanceScore.
        // Ignored entirely server-side when EnableHybridSearch is on (see
        // RagOptions.EnableHybridSearch's doc comment) — harmless to still
        // compute/pass.
        var searchFloor = ragOptions.EnableAdaptiveThreshold
            ? Math.Min(ragOptions.AdaptiveFloorScore, ragOptions.MinRelevanceScore)
            : ragOptions.MinRelevanceScore;

        var candidates = new List<VectorSearchResult>();
        await foreach (var result in vectorStore
            .SearchAsync(
                VectorCollection.Documents,
                queryEmbedding,
                ragOptions.TopK,
                searchFloor,
                ragOptions.EnableHybridSearch ? queryText : null,
                cancellationToken)
            .ConfigureAwait(false))
        {
            candidates.Add(result);
        }

        // See RagOptions.EnableHybridSearch's doc comment — a fused (RRF)
        // score is rank-based, not cosine similarity, so the
        // cosine-calibrated adaptive-threshold math below doesn't apply to
        // it; ApplyHybridRelevanceFilter is the rescaled equivalent.
        var kept = ragOptions.EnableHybridSearch
            ? ApplyHybridRelevanceFilter(candidates, ragOptions)
            : ragOptions.EnableAdaptiveThreshold
                ? ApplyAdaptiveThreshold(candidates, ragOptions)
                : candidates;

        return kept.Select(result => new DocumentChunk
        {
            Id = result.Id,
            SourceId = result.Metadata.SourcePath,
            Content = result.Content ?? string.Empty,
            Metadata = result.Metadata
        }).ToList();
    }

    /// <summary>
    /// Keeps only candidates within <see cref="RagOptions.AdaptiveThresholdMargin"/>
    /// of THIS query's own best-scoring candidate — see
    /// <see cref="RagOptions.EnableAdaptiveThreshold"/>'s doc comment for
    /// why a per-query relative cutoff catches what a flat floor can't.
    /// Doesn't assume the vector store returns results pre-sorted by
    /// score — takes the max explicitly rather than candidates[0].
    /// </summary>
    private static List<VectorSearchResult> ApplyAdaptiveThreshold(
        List<VectorSearchResult> candidates,
        RagOptions ragOptions)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var topScore = candidates.Max(c => c.Score);
        var cutoff = topScore - ragOptions.AdaptiveThresholdMargin;

        return candidates
            .Where(c => c.Score >= cutoff && c.Score >= ragOptions.AdaptiveFloorScore)
            .ToList();
    }

    /// <summary>
    /// Hybrid-search equivalent of <see cref="ApplyAdaptiveThreshold"/> —
    /// see <see cref="RagOptions.HybridRelevanceRatio"/>'s doc comment for
    /// why this uses a ratio (scale-free) instead of an absolute margin
    /// (calibrated to cosine similarity, doesn't transfer to a fused RRF
    /// score). Same "don't assume pre-sorted" caution as
    /// <see cref="ApplyAdaptiveThreshold"/> — takes the max explicitly.
    /// Internal (not private): <see cref="Looma.Application.UseCases.AnswerUseCase"/>
    /// also does its own inline hybrid retrieval (see that class's doc
    /// comment for why it doesn't otherwise go through this class) and
    /// needs the identical filter rather than a second, potentially
    /// drifting copy of the same logic.
    /// </summary>
    internal static List<VectorSearchResult> ApplyHybridRelevanceFilter(
        List<VectorSearchResult> candidates,
        RagOptions ragOptions)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var topScore = candidates.Max(c => c.Score);
        var cutoff = topScore * ragOptions.HybridRelevanceRatio;

        return candidates.Where(c => c.Score >= cutoff).ToList();
    }
}
