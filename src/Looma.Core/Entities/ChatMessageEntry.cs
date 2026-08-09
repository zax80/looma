namespace Looma.Core.Entities;

public enum ChatMessageRole
{
    User,
    Assistant
}

/// <summary>One turn in a <see cref="ChatSession"/>.</summary>
public sealed record ChatMessageEntry
{
    public required string Id { get; init; }
    public required ChatMessageRole Role { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Populated on Assistant messages only — the chunks retrieved for this turn.</summary>
    public IReadOnlyList<DocumentChunk>? Citations { get; init; }
}
