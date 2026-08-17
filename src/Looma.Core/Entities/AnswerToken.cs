namespace Looma.Core.Entities;

/// <summary>One streamed token/fragment of a generated answer, with citations attached on the final token.</summary>
public sealed record AnswerToken
{
    public required string Text { get; init; }
    public required bool IsFinal { get; init; }

    /// <summary>Populated on the final token only.</summary>
    public IReadOnlyList<DocumentChunk>? Citations { get; init; }

    /// <summary>
    /// True when <see cref="Text"/> is a progress message ("Searching local
    /// documents...", "No local match found — searching the web...") rather
    /// than actual answer content — only ever emitted by
    /// <c>AnswerUseCase</c>/<c>ChatCompletionUseCase</c> when
    /// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>
    /// is on, since that's the only path with real stages worth narrating
    /// (plain streaming already shows progress token-by-token on its own).
    /// Always false alongside <see cref="IsFinal"/> — a status update is
    /// never the last token. Consumers that don't care can treat it like any
    /// other non-final token (it's still a plain, readable sentence); the
    /// point is letting ones that DO care (see <c>AnswerCommand</c>) render
    /// it distinctly instead of concatenating it into the answer text.
    /// </summary>
    public bool IsStatus { get; init; }
}
