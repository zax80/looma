using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>
/// The single vector storage abstraction. Qdrant is the only implementation
/// (see architecture rules) — do not add a second one "for simplicity".
/// Collection-aware from the start: every call is scoped to a
/// <see cref="VectorCollection"/>, never a single flat store.
/// </summary>
public interface IVectorStore
{
    Task EnsureCollectionAsync(
        VectorCollection collection,
        int dimensions,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        VectorCollection collection,
        IReadOnlyList<VectorRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>Streams matches as they are found — not a buffer-then-return list.</summary>
    IAsyncEnumerable<VectorSearchResult> SearchAsync(
        VectorCollection collection,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float? minRelevanceScore = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        VectorCollection collection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default);

    Task<long> CountAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default);
}
