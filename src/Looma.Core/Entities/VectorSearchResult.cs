namespace Looma.Core.Entities;

/// <summary>A single scored match from an <see cref="Looma.Core.Abstractions.IVectorStore"/> search.</summary>
public sealed record VectorSearchResult
{
    public required string Id { get; init; }
    public required float Score { get; init; }
    public string? Content { get; init; }
    public required ChunkMetadata Metadata { get; init; }
}
