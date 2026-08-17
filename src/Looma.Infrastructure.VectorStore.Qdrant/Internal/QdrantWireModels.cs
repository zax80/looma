using System.Text.Json.Serialization;

namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

// Wire-format DTOs for Qdrant's HTTP REST API (v1). Internal on purpose —
// nothing outside this assembly should ever see Qdrant's request/response
// shapes; that would leak a vendor concept through the IVectorStore
// abstraction boundary.

internal sealed class CreateCollectionRequest
{
    [JsonPropertyName("vectors")]
    public required VectorParams Vectors { get; init; }
}

internal sealed class VectorParams
{
    [JsonPropertyName("size")]
    public required int Size { get; init; }

    [JsonPropertyName("distance")]
    public required string Distance { get; init; }
}

/// <summary>
/// Collection creation request for the hybrid (named dense + named sparse)
/// schema — deliberately a separate type from <see cref="CreateCollectionRequest"/>
/// rather than one type covering both shapes: a collection either has a
/// single unnamed vector (existing, non-hybrid collections) or named
/// vectors (hybrid) — never a value in between, and keeping the two
/// request shapes as distinct types makes that impossible to blur by
/// accident.
/// </summary>
internal sealed class CreateHybridCollectionRequest
{
    [JsonPropertyName("vectors")]
    public required Dictionary<string, VectorParams> Vectors { get; init; }

    [JsonPropertyName("sparse_vectors")]
    public required Dictionary<string, SparseVectorParams> SparseVectors { get; init; }
}

/// <summary>
/// <c>modifier: "idf"</c> is required for BM25-style relevance — Qdrant's
/// server-side BM25 sparse vectors intentionally exclude the IDF
/// (inverse document frequency) term, applying it at QUERY time instead
/// based on this setting, computed from the live collection rather than
/// baked into each document at index time (so it stays correct as the
/// collection grows). Confirmed via Qdrant's own docs — not guessed.
/// </summary>
internal sealed class SparseVectorParams
{
    [JsonPropertyName("modifier")]
    public string Modifier { get; init; } = "idf";
}

internal sealed class CollectionInfoResponse
{
    [JsonPropertyName("result")]
    public CollectionInfoResult? Result { get; init; }
}

internal sealed class CollectionInfoResult
{
    [JsonPropertyName("points_count")]
    public long? PointsCount { get; init; }

    [JsonPropertyName("config")]
    public CollectionConfigInfo? Config { get; init; }
}

/// <summary>
/// Only the one nested field <c>QdrantVectorStore</c> actually needs —
/// whether this EXISTING collection already has a sparse vector configured
/// — used to detect the old (pre-hybrid) schema on an already-created
/// collection. See <c>QdrantVectorStore.EnsureCollectionAsync</c>'s
/// migration handling.
/// </summary>
internal sealed class CollectionConfigInfo
{
    [JsonPropertyName("params")]
    public CollectionParamsInfo? Params { get; init; }
}

internal sealed class CollectionParamsInfo
{
    [JsonPropertyName("sparse_vectors")]
    public Dictionary<string, object>? SparseVectors { get; init; }
}

/// <summary>Response shape of Qdrant's root <c>GET /</c> endpoint — used only to read <see cref="Version"/> for the minimum-version check hybrid search needs (server-side BM25 requires Qdrant 1.15.2+).</summary>
internal sealed class QdrantRootResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

internal sealed class UpsertPointsRequest
{
    [JsonPropertyName("points")]
    public required IReadOnlyList<PointStruct> Points { get; init; }
}

internal sealed class UpsertHybridPointsRequest
{
    [JsonPropertyName("points")]
    public required IReadOnlyList<HybridPointStruct> Points { get; init; }
}

internal sealed class PointStruct
{
    /// <summary>Must be an unsigned integer or UUID string — a Qdrant constraint, not ours. Callers should populate <c>VectorRecord.Id</c> with a UUID string.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("vector")]
    public required float[] Vector { get; init; }

    [JsonPropertyName("payload")]
    public required QdrantPayload Payload { get; init; }
}

/// <summary>
/// Upsert point shape for a hybrid (named dense + named sparse) collection —
/// see <see cref="CreateHybridCollectionRequest"/>'s doc comment for why
/// this is a distinct type from the plain <see cref="PointStruct"/> rather
/// than one type covering both.
/// </summary>
internal sealed class HybridPointStruct
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("vector")]
    public required HybridVectors Vector { get; init; }

    [JsonPropertyName("payload")]
    public required QdrantPayload Payload { get; init; }
}

internal sealed class HybridVectors
{
    [JsonPropertyName("dense")]
    public required float[] Dense { get; init; }

    [JsonPropertyName("sparse")]
    public required Bm25Document Sparse { get; init; }
}

/// <summary>
/// Qdrant's "Inference Object" shape for server-side BM25 — sending the raw
/// text plus this model name is enough; Qdrant tokenizes and weights it
/// into a sparse vector itself (no client-side tokenizer/vocabulary needed).
/// Used both at upsert time (indexing this text) and query time (indexing
/// the search query the same way, so the two are comparable).
/// </summary>
internal sealed class Bm25Document
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = "qdrant/bm25";
}

/// <summary>Flattened, typed payload stored alongside each vector — mirrors <see cref="Looma.Core.Entities.ChunkMetadata"/> plus content.</summary>
internal sealed class QdrantPayload
{
    [JsonPropertyName("source_path")]
    public required string SourcePath { get; init; }

    [JsonPropertyName("media_type")]
    public required string MediaType { get; init; }

    [JsonPropertyName("chunk_index")]
    public required int ChunkIndex { get; init; }

    [JsonPropertyName("start_line")]
    public int? StartLine { get; init; }

    [JsonPropertyName("end_line")]
    public int? EndLine { get; init; }

    [JsonPropertyName("start_time_ticks")]
    public long? StartTimeTicks { get; init; }

    [JsonPropertyName("end_time_ticks")]
    public long? EndTimeTicks { get; init; }

    [JsonPropertyName("indexed_at_utc")]
    public required DateTimeOffset IndexedAtUtc { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

internal sealed class SearchPointsRequest
{
    [JsonPropertyName("vector")]
    public required float[] Vector { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonPropertyName("score_threshold")]
    public float? ScoreThreshold { get; init; }

    [JsonPropertyName("with_payload")]
    public bool WithPayload { get; init; } = true;
}

internal sealed class SearchPointsResponse
{
    [JsonPropertyName("result")]
    public IReadOnlyList<ScoredPoint>? Result { get; init; }
}

internal sealed class ScoredPoint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("score")]
    public required float Score { get; init; }

    [JsonPropertyName("payload")]
    public QdrantPayload? Payload { get; init; }
}

internal sealed class DeletePointsRequest
{
    [JsonPropertyName("points")]
    public required IReadOnlyList<string> Points { get; init; }
}

internal sealed class CountPointsRequest
{
    [JsonPropertyName("exact")]
    public bool Exact { get; init; } = true;
}

internal sealed class CountPointsResponse
{
    [JsonPropertyName("result")]
    public CountPointsResult? Result { get; init; }
}

internal sealed class CountPointsResult
{
    [JsonPropertyName("count")]
    public long Count { get; init; }
}

// ---- Query API (/points/query) — used only for hybrid search. Plain
// dense-only search keeps using /points/search (SearchPointsRequest above)
// unchanged, since that endpoint is simpler and every existing non-hybrid
// collection/caller already depends on its exact behavior. ----

internal sealed class QueryPointsRequest
{
    [JsonPropertyName("prefetch")]
    public required IReadOnlyList<PrefetchQuery> Prefetch { get; init; }

    [JsonPropertyName("query")]
    public required FusionQuery Query { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }

    [JsonPropertyName("with_payload")]
    public bool WithPayload { get; init; } = true;
}

/// <summary>
/// One leg of a hybrid query — <see cref="Query"/> is deliberately typed
/// <c>object</c>: at runtime it's either a <c>float[]</c> (the dense leg)
/// or a <see cref="Bm25Document"/> (the sparse leg), and
/// <c>System.Text.Json</c> serializes an <c>object</c>-typed member using
/// its actual runtime type, which is exactly the "either/or" shape
/// Qdrant's API expects here — no custom converter needed.
/// </summary>
internal sealed class PrefetchQuery
{
    [JsonPropertyName("query")]
    public required object Query { get; init; }

    [JsonPropertyName("using")]
    public required string Using { get; init; }

    [JsonPropertyName("limit")]
    public required int Limit { get; init; }
}

/// <summary>
/// Reciprocal Rank Fusion with Qdrant's own defaults (k=60, equal weights
/// across both prefetch legs) — not exposed as a tuning knob yet; revisit
/// if real testing shows a reason to weight keyword vs. semantic matches
/// differently.
/// </summary>
internal sealed class FusionQuery
{
    [JsonPropertyName("rrf")]
    public object Rrf { get; init; } = new { };
}

internal sealed class QueryPointsResponse
{
    [JsonPropertyName("result")]
    public QueryPointsResult? Result { get; init; }
}

/// <summary>
/// Unlike <c>/points/search</c>'s response (a flat array directly under
/// <c>result</c>), <c>/points/query</c> nests results one level deeper,
/// under <c>result.points</c> — confirmed against Qdrant's own
/// documentation, not guessed.
/// </summary>
internal sealed class QueryPointsResult
{
    [JsonPropertyName("points")]
    public IReadOnlyList<ScoredPoint>? Points { get; init; }
}
