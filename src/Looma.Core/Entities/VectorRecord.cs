namespace Looma.Core.Entities;

/// <summary>A single vector + payload to upsert into an <see cref="Looma.Core.Abstractions.IVectorStore"/> collection.</summary>
public sealed record VectorRecord
{
    public required string Id { get; init; }
    public required ReadOnlyMemory<float> Embedding { get; init; }
    public required ChunkMetadata Metadata { get; init; }

    /// <summary>Original content, stored alongside the vector for retrieval without a second round-trip.</summary>
    public string? Content { get; init; }
}
