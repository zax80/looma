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
    /// <param name="enableHybridSearch">
    /// When true, the collection is created with a named dense vector PLUS
    /// a named sparse (BM25, server-side-computed by Qdrant) vector instead
    /// of a single unnamed dense vector — required for
    /// <see cref="SearchAsync"/>'s <c>queryText</c> parameter to do
    /// anything. Callers must pass the SAME value here as they later pass
    /// to <see cref="UpsertAsync"/> and (implicitly, via a non-null
    /// <c>queryText</c>) <see cref="SearchAsync"/> for this collection — a
    /// collection's vector schema is fixed at creation time, so this can't
    /// be decided differently call to call. See <c>QdrantVectorStore</c>'s
    /// doc comment for why this is a real, breaking schema difference (not
    /// something addable to an existing collection in place) and what
    /// happens if an existing collection was created the other way.
    /// </param>
    Task EnsureCollectionAsync(
        VectorCollection collection,
        int dimensions,
        bool enableHybridSearch = false,
        CancellationToken cancellationToken = default);

    /// <param name="enableHybridSearch">
    /// Must match whatever <see cref="EnsureCollectionAsync"/> was called
    /// with for this same collection — see its doc comment. When true,
    /// each record's <see cref="VectorRecord.Content"/> is sent as a BM25
    /// document alongside its dense embedding, so both retrieval modes see
    /// the same content.
    /// </param>
    Task UpsertAsync(
        VectorCollection collection,
        IReadOnlyList<VectorRecord> records,
        bool enableHybridSearch = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams matches as they are found — not a buffer-then-return list.
    /// </summary>
    /// <param name="queryText">
    /// When non-null/non-empty AND this collection was created with
    /// <c>enableHybridSearch: true</c> (see <see cref="EnsureCollectionAsync"/>),
    /// performs a hybrid dense+BM25 query (Qdrant's Query API, fused via
    /// Reciprocal Rank Fusion) instead of a pure dense-vector search. Left
    /// null (the default), behavior is unchanged from a plain dense search.
    /// <see cref="VectorSearchResult.Score"/> means something different
    /// under fusion (an RRF rank-based score, not cosine similarity) — see
    /// <c>RagOptions.EnableHybridSearch</c>'s doc comment for why
    /// <paramref name="minRelevanceScore"/> is ignored when this is used.
    /// </param>
    IAsyncEnumerable<VectorSearchResult> SearchAsync(
        VectorCollection collection,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float? minRelevanceScore = null,
        string? queryText = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        VectorCollection collection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default);

    Task<long> CountAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every point in <paramref name="collection"/> (the collection
    /// itself is recreated lazily by the next <see cref="EnsureCollectionAsync"/>
    /// call). A no-op if the collection doesn't exist. Exists specifically
    /// for recovering from stale/duplicate data — e.g. re-indexing with
    /// <c>looma index --clear</c> — without needing to hit the vector
    /// store's own admin API by hand.
    /// </summary>
    Task ClearCollectionAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default);
}
