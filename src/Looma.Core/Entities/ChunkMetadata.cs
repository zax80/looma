namespace Looma.Core.Entities;

/// <summary>
/// Typed metadata for a chunk. Deliberately not a
/// <c>Dictionary&lt;string, object&gt;</c> — JSON round-tripping a loose
/// dictionary deserializes values as <see cref="System.Text.Json.JsonElement"/>
/// rather than native types, which caused a runtime cast bug in a prior
/// version. Extend this record instead of reintroducing a loose bag.
/// </summary>
public sealed record ChunkMetadata
{
    public required string SourcePath { get; init; }

    public required MediaType MediaType { get; init; }

    public required int ChunkIndex { get; init; }

    /// <summary>Line range in the source file/document, when applicable (text/PDF/docx/md/csv).</summary>
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }

    /// <summary>Timestamp range in the source recording, when applicable (audio transcripts).</summary>
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }

    public required DateTimeOffset IndexedAtUtc { get; init; }
}
