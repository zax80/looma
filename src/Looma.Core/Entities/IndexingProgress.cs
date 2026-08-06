namespace Looma.Core.Entities;

/// <summary>Streamed progress event emitted while indexing a folder — one per file, not buffered.</summary>
public sealed record IndexingProgress
{
    public required string FilePath { get; init; }
    public required IndexingStatus Status { get; init; }
    public int ChunksIndexed { get; init; }
    public int? FileIndex { get; init; }
    public int? TotalFiles { get; init; }
    public string? ErrorMessage { get; init; }
}
