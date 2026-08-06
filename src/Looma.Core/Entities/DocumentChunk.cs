namespace Looma.Core.Entities;

/// <summary>
/// A chunk of text-embeddable content bound for the <c>documents</c> vector
/// collection — produced from real chunking-with-overlap, never truncation,
/// regardless of the originating media type (text, audio transcript, or
/// image caption/OCR).
/// </summary>
public sealed record DocumentChunk
{
    public required string Id { get; init; }

    /// <summary>Identifier of the originating source (file path or stable source id).</summary>
    public required string SourceId { get; init; }

    public required string Content { get; init; }

    public required ChunkMetadata Metadata { get; init; }

    /// <summary>Text-embedding-space vector. Null until embedded.</summary>
    public ReadOnlyMemory<float>? Embedding { get; init; }
}
