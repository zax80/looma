using System.Text.Json.Serialization;
using Looma.Core.Entities;

namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

// Wire-format DTOs for the semantic-fallback layer of the answer cache
// (Internal — same reasoning as QdrantWireModels.cs: nothing outside this
// assembly should see Qdrant's request/response shapes). Kept separate from
// QdrantWireModels' point/payload types because the payload shape here
// (question/answer/citations) has nothing to do with a document chunk's.

internal sealed class AnswerCacheUpsertRequest
{
    [JsonPropertyName("points")]
    public required IReadOnlyList<AnswerCachePointStruct> Points { get; init; }
}

internal sealed class AnswerCachePointStruct
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("vector")]
    public required float[] Vector { get; init; }

    [JsonPropertyName("payload")]
    public required AnswerCachePayload Payload { get; init; }
}

internal sealed class AnswerCachePayload
{
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    [JsonPropertyName("answer_text")]
    public required string AnswerText { get; init; }

    [JsonPropertyName("citations")]
    public required IReadOnlyList<DocumentChunk> Citations { get; init; }

    [JsonPropertyName("documents_version")]
    public required long DocumentsVersion { get; init; }

    [JsonPropertyName("created_at_utc")]
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class AnswerCacheSearchRequest
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

internal sealed class AnswerCacheSearchResponse
{
    [JsonPropertyName("result")]
    public IReadOnlyList<AnswerCacheScoredPoint>? Result { get; init; }
}

internal sealed class AnswerCacheScoredPoint
{
    [JsonPropertyName("score")]
    public required float Score { get; init; }

    [JsonPropertyName("payload")]
    public AnswerCachePayload? Payload { get; init; }
}

/// <summary>Exact-match layer's on-disk shape — one entry per normalized question, in a single JSON file.</summary>
internal sealed class ExactCacheEntry
{
    public required string AnswerText { get; init; }

    public required IReadOnlyList<DocumentChunk> Citations { get; init; }

    public required long DocumentsVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
