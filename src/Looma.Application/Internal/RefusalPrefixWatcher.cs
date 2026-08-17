using System.Text;

namespace Looma.Application.Internal;

/// <summary>
/// Buffers streamed text only while it could still turn into the exact
/// refusal sentence (<see cref="GroundedAnswer.NoAnswerSentence"/>) — the
/// moment accumulated text can no longer match, it's flushed and every
/// chunk after that passes through untouched. This is what lets
/// <c>AnswerUseCase</c>/<c>ChatCompletionUseCase</c> stream a real answer
/// live (the common case) while still catching the much shorter,
/// fixed-format refusal case before ever showing it to the user — see
/// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>'s
/// doc comment for why that decision has to happen at all.
///
/// Pure string state — no I/O, no async — so this is fully unit-testable,
/// including chunk-boundary edge cases: a real streaming response rarely
/// lines up one whole word per chunk, and a genuinely short answer that
/// happens to be a strict prefix of the refusal sentence (e.g. the model
/// says "The provided context" and simply stops) must still be treated as
/// real content, not a refusal — only an EXACT match at stream end counts.
/// </summary>
internal sealed class RefusalPrefixWatcher
{
    private readonly StringBuilder _pending = new();
    private bool _diverged;

    /// <summary>
    /// Feed the next streamed chunk. Returns the text safe to show the user
    /// right now — empty while still buffering a possible-refusal prefix,
    /// otherwise the chunk (plus anything held back, released all at once
    /// the moment divergence is first detected).
    /// </summary>
    public string Observe(string chunk)
    {
        if (_diverged)
        {
            return chunk;
        }

        _pending.Append(chunk);
        var candidate = _pending.ToString();

        // TrimStart, not Trim — trailing whitespace only matters once the
        // stream is fully done (see IsConfirmedRefusal), but leading
        // whitespace shouldn't stop a prefix match from the first chunk.
        if (GroundedAnswer.NoAnswerSentence.StartsWith(candidate.TrimStart(), StringComparison.Ordinal))
        {
            // Still could become the refusal sentence exactly — or already
            // does at this exact length. Either way, resolved at stream end
            // via IsConfirmedRefusal, not here: more tokens could still
            // arrive even after an exact length match, which would mean
            // it's NOT actually a refusal after all (see class doc comment).
            return string.Empty;
        }

        // Diverged — this can never become the refusal sentence. Release
        // everything buffered so far and stop checking from here on.
        _diverged = true;
        _pending.Clear();
        return candidate;
    }

    /// <summary>
    /// Call once the stream ends. True only if everything ever buffered,
    /// trimmed, is an EXACT match for the refusal sentence — a partial
    /// match the model simply stopped short on is real (if strange) content,
    /// not a refusal, and must still be shown via <see cref="FlushRemaining"/>.
    /// </summary>
    public bool IsConfirmedRefusal =>
        !_diverged && string.Equals(_pending.ToString().Trim(), GroundedAnswer.NoAnswerSentence, StringComparison.Ordinal);

    /// <summary>
    /// Whatever's still buffered and ISN'T a confirmed refusal — safe to
    /// call unconditionally after the stream ends (returns empty when
    /// <see cref="IsConfirmedRefusal"/> is true, rather than relying on the
    /// caller to check that first): releases genuine content that happened
    /// to still look like a possible refusal prefix right up until the
    /// model stopped generating, without ever releasing the refusal
    /// sentence itself.
    /// </summary>
    public string FlushRemaining()
    {
        if (_diverged || IsConfirmedRefusal)
        {
            return string.Empty;
        }

        var remaining = _pending.ToString();
        _pending.Clear();
        return remaining;
    }
}
