namespace Looma.Core.Entities;

/// <summary>One streamed token/fragment of a generated answer, with citations attached on the final token.</summary>
public sealed record AnswerToken
{
    public required string Text { get; init; }
    public required bool IsFinal { get; init; }

    /// <summary>Populated on the final token only.</summary>
    public IReadOnlyList<DocumentChunk>? Citations { get; init; }
}
