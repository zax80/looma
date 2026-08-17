using Looma.Core.Entities;

namespace Looma.Application.Internal;

/// <summary>
/// Bounds how much conversation history <c>ChatCompletionUseCase.BuildPrompt</c>
/// sends to the model — nothing was previously stopping the FULL, entire
/// history (every turn since the conversation started) from being resent
/// verbatim on every single turn.
///
/// A real, reproduced bug this fixes: a long chat session's accumulated
/// history (several failed turns, each contributing both a user message
/// and a synthetic "(No answer was generated...)" reply, plus full
/// web-search citation snippets from an earlier turn) grew past the
/// model's actual context window. Once it did, EVERY subsequent turn
/// broke — including ones about a totally unrelated topic — because the
/// model was silently handed an oversized prompt and, on the local model
/// tested against, fell into a token-repetition loop trying to generate a
/// response at all. See <c>IdleTimeoutStreaming</c>'s doc comment for the
/// timeout that turns that failure mode into a visible error instead of a
/// silent hang — that's a safety net; this class is the fix for the
/// actual cause.
///
/// Deliberately provider-agnostic. An earlier version of this fix baked a
/// context size into a dedicated Ollama-specific model via its
/// <c>/api/create</c> endpoint, which worked but only for Ollama, and only
/// by creating extra models behind the scenes — a real, working fix, just
/// the wrong layer for it: nothing about "don't send more than the model
/// can hold" is Ollama-specific, and Looma may not always talk to Ollama.
/// Bounding what actually gets SENT, here, works no matter what serves the
/// model.
///
/// No real tokenizer is wired into this codebase yet (same reasoning as
/// <see cref="Looma.Application.Configuration.RagOptions.ChunkSize"/>'s
/// doc comment) — this budgets on character count, not real tokens, using
/// a deliberately conservative characters-per-token estimate. Erring
/// toward trimming a bit more than strictly necessary is the safe
/// direction to be wrong in; erring the other way reproduces the exact bug
/// this exists to fix.
/// </summary>
internal static class ConversationHistoryBudget
{
    // English text commonly runs ~4 chars/token; this app handles Cyrillic
    // input throughout (seen repeatedly in real testing), which tokenizes
    // less efficiently per character than plain Latin script — 3.0 stays
    // conservative across both instead of being tuned to whichever
    // happens to dominate a given conversation.
    private const double CharsPerToken = 3.0;

    /// <summary>
    /// Returns the newest contiguous run of <paramref name="history"/> that
    /// fits within the model's context window once
    /// <paramref name="reservedChars"/> — everything ELSE going into the
    /// same prompt (system prompt, retrieved context, the current
    /// question) — is accounted for. Always keeps at least the single most
    /// recent entry, even if it alone doesn't fit within budget: better to
    /// hand the model slightly more than it can ideally hold than to
    /// silently drop the one turn a follow-up is most likely to refer to.
    /// Returns <paramref name="history"/> unchanged when
    /// <paramref name="modelContextSizeTokens"/> is null — nothing to
    /// budget against without it.
    /// </summary>
    public static List<ChatMessageEntry> Trim(
        IReadOnlyList<ChatMessageEntry> history, int? modelContextSizeTokens, int reservedChars)
    {
        if (modelContextSizeTokens is not { } contextTokens || history.Count == 0)
        {
            return history.ToList();
        }

        var historyBudgetChars = (int)(contextTokens * CharsPerToken) - reservedChars;
        if (historyBudgetChars <= 0)
        {
            return [history[^1]];
        }

        var kept = new List<ChatMessageEntry>();
        var used = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var entryChars = history[i].Text.Length;
            if (used + entryChars > historyBudgetChars && kept.Count > 0)
            {
                break;
            }

            kept.Add(history[i]);
            used += entryChars;
        }

        kept.Reverse();
        return kept;
    }
}
