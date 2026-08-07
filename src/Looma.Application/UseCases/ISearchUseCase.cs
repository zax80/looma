using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Embeds a query and streams scored matches from a vector collection.
///
/// Known gap: the query is always embedded with the text embedding model
/// (<c>Models.EmbeddingModel</c>, nomic-embed-text, 768-dim), regardless of
/// <c>collection</c>. That's correct for <see cref="VectorCollection.Documents"/>,
/// but calling this against <see cref="VectorCollection.Images"/> (CLIP,
/// 512-dim) will send a dimension-mismatched vector and fail loudly at
/// Qdrant rather than search meaningfully — there's no CLIP *text* encoder
/// wired up yet for query-side text→image search, only the image encoder
/// used at ingestion time. Not fixed here; flagging so it fails
/// understandably rather than looking like a silent bug.
/// </summary>
public interface ISearchUseCase
{
    /// <param name="minRelevanceScore">
    /// Overrides <c>RAG.MinRelevanceScore</c> from config for this call only
    /// — pass 0 to see every top-K candidate regardless of the configured
    /// threshold. Exists specifically so retrieval can be inspected
    /// directly (e.g. via <c>looma search</c>) instead of only ever seeing
    /// results already filtered the same way <c>answer</c> filters them.
    /// Null (the default) uses the configured threshold, same as before.
    /// </param>
    IAsyncEnumerable<VectorSearchResult> SearchAsync(
        string query,
        VectorCollection collection = VectorCollection.Documents,
        int topK = 5,
        float? minRelevanceScore = null,
        CancellationToken cancellationToken = default);
}
