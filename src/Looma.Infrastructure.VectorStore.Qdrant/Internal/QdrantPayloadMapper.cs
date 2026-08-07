using Looma.Core.Entities;

namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

/// <summary>Translates between <see cref="ChunkMetadata"/> and Qdrant's flattened payload shape.</summary>
internal static class QdrantPayloadMapper
{
    public static QdrantPayload ToPayload(ChunkMetadata metadata, string? content) => new()
    {
        SourcePath = metadata.SourcePath,
        MediaType = metadata.MediaType.ToString(),
        ChunkIndex = metadata.ChunkIndex,
        StartLine = metadata.StartLine,
        EndLine = metadata.EndLine,
        StartTimeTicks = metadata.StartTime?.Ticks,
        EndTimeTicks = metadata.EndTime?.Ticks,
        IndexedAtUtc = metadata.IndexedAtUtc,
        Content = content
    };

    public static ChunkMetadata ToMetadata(QdrantPayload payload) => new()
    {
        SourcePath = payload.SourcePath,
        MediaType = Enum.Parse<MediaType>(payload.MediaType),
        ChunkIndex = payload.ChunkIndex,
        StartLine = payload.StartLine,
        EndLine = payload.EndLine,
        StartTime = payload.StartTimeTicks is { } startTicks ? TimeSpan.FromTicks(startTicks) : null,
        EndTime = payload.EndTimeTicks is { } endTicks ? TimeSpan.FromTicks(endTicks) : null,
        IndexedAtUtc = payload.IndexedAtUtc
    };
}
