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

    /// <summary>
    /// Populated on User messages only, when this turn had an image or
    /// document attached — just the filename, e.g. "invoice.pdf", for
    /// display purposes. See <see cref="AttachmentContent"/> for the
    /// actual extracted material.
    /// </summary>
    public string? AttachmentLabel { get; init; }

    /// <summary>
    /// Populated on User messages only, when this turn had an image or
    /// document attached — the actual caption (image) or extracted text
    /// (document) used as grounding material for this turn. This used to
    /// be purely ephemeral — discarded the instant the turn finished,
    /// which meant a later question about a fact from that same
    /// attachment that Looma hadn't happened to restate out loud was
    /// simply unanswerable (a real case hit in testing: asking "who's the
    /// author?" two turns after attaching an image whose caption included
    /// the author, but whose answer about it didn't). Now persisted so
    /// <c>ChatCompletionUseCase</c> can re-surface it as "sticky" context
    /// on later turns in THIS SAME session (see its
    /// BuildStickyAttachmentsBlock) — never embedded for retrieval and
    /// never added to the global document index, so this is session-local
    /// memory, not indexing. A different chat session about the same file
    /// starts with none of it; that's deliberate, matching the original
    /// "ask about it live" design this extends rather than reverses.
    /// </summary>
    public string? AttachmentContent { get; init; }
}
