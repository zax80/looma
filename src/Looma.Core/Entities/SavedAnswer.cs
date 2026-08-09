namespace Looma.Core.Entities;

/// <summary>
/// A "reusable artefact" — one generated answer pinned outside the chat
/// session it came from, so it can be revisited without re-asking or
/// scrolling back through a whole conversation.
/// </summary>
public sealed record SavedAnswer
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Question { get; init; }
    public required string AnswerText { get; init; }
    public required IReadOnlyList<DocumentChunk> Citations { get; init; }
    public required DateTimeOffset SavedAtUtc { get; init; }

    /// <summary>The chat session this was pinned from, if any — informational only, not a live link back.</summary>
    public string? SourceSessionId { get; init; }
}
