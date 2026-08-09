namespace Looma.Core.Entities;

/// <summary>A multi-turn conversation — the unit <c>IChatSessionStore</c> persists.</summary>
public sealed record ChatSession
{
    public required string Id { get; init; }

    /// <summary>Derived from the first user message the first time one is appended — never set directly by callers.</summary>
    public required string Title { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public required IReadOnlyList<ChatMessageEntry> Messages { get; init; }
}

/// <summary>Lightweight listing shape — no message bodies, for a session list UI.</summary>
public sealed record ChatSessionSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
