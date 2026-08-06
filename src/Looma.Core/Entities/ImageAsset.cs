namespace Looma.Core.Entities;

/// <summary>
/// An image bound for the <c>images</c> vector collection (CLIP space).
/// Kept separate from <see cref="DocumentChunk"/> — CLIP vectors and text
/// embeddings live in different, non-comparable spaces and must never be
/// mixed into the same collection.
/// </summary>
public sealed record ImageAsset
{
    public required string Id { get; init; }

    public required string SourcePath { get; init; }

    /// <summary>CLIP-space vector. Null until embedded.</summary>
    public ReadOnlyMemory<float>? ClipEmbedding { get; init; }

    /// <summary>Caption/OCR text also gets chunked and embedded into <c>documents</c> separately.</summary>
    public string? Caption { get; init; }
    public string? OcrText { get; init; }

    public required DateTimeOffset IndexedAtUtc { get; init; }
}
