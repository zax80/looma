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

internal sealed class CollectionInfoResponse
{
    [JsonPropertyName("result")]
    public CollectionInfoResult? Result { get; init; }
}

internal sealed class CollectionInfoResult
{
    [JsonPropertyName("points_count")]
    public long? PointsCount { get; init; }
}

internal sealed class UpsertPointsRequest
{
    [JsonPropertyName("points")]
    public required IReadOnlyList<PointStruct> Points { get; init; }
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
