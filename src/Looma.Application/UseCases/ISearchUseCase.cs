using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Embeds a query and streams scored matches from a vector collection.
///
/// <see cref="VectorCollection.Documents"/> queries always go through the
/// text embedding model (<c>Models.EmbeddingModel</c>, nomic-embed-text).
/// <see cref="VectorCollection.Images"/> queries go through CLIP's TEXT
/// tower instead (<c>Models.ImageEmbeddingModel.TextTower</c>, via
/// <see cref="Looma.Core.Abstractions.ITextToImageEmbeddingGenerator"/>) —
/// the paired encoder to the image tower used at ingestion time, landing
/// in the same 512-dim CLIP space so the comparison is meaningful. If
/// <c>TextTower</c> isn't configured (it's optional — see
/// <c>docs/model-setup.md</c>), an images-collection query fails with a
/// clear "not configured" error rather than a confusing Qdrant
/// dimension-mismatch — see <c>SearchUseCase</c>.
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
