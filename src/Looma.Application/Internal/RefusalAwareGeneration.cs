using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Looma.Application.Internal;

/// <summary>
/// Mutable output populated by <see cref="RefusalAwareGeneration.StreamAsync"/>
/// as a side effect while its returned sequence is enumerated — every
/// field is meaningless mid-enumeration and must only be read AFTER the
/// caller's <c>await foreach</c> over that sequence has fully completed
/// (by which point the iterator has finished and every mutation already
/// happened).
/// </summary>
internal sealed class RefusalAwareGenerationOutcome
{
    public bool WasRefusal { get; internal set; }
    public string FullText { get; internal set; } = string.Empty;
}

/// <summary>
/// Combines <see cref="IdleTimeoutStreaming"/> and <see cref="RefusalPrefixWatcher"/>:
/// streams a chat response live, chunk by chunk, except for whatever text
/// the watcher is still holding back as a possible refusal-sentence prefix
/// — callers only ever see text that's confirmed to be real content, in
/// real time, exactly as it's produced. Shared by
/// <see cref="Looma.Application.UseCases.AnswerUseCase"/> and
/// <see cref="Looma.Application.UseCases.ChatCompletionUseCase"/> so the two
/// use cases can't silently drift apart on how this decision streams —
/// same extraction reasoning as <see cref="RagRetrieval"/>/<see cref="WebSearchFallback"/>.
/// </summary>
internal static class RefusalAwareGeneration
{
    public static async IAsyncEnumerable<string> StreamAsync(
        IChatClient chatClient,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        int idleTimeoutSeconds,
        RefusalAwareGenerationOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var watcher = new RefusalPrefixWatcher();
        var fullText = new StringBuilder();

        await foreach (var chunk in IdleTimeoutStreaming
            .StreamAsync(chatClient, messages, chatOptions, idleTimeoutSeconds, cancellationToken)
            .ConfigureAwait(false))
        {
            fullText.Append(chunk);
            var released = watcher.Observe(chunk);
            if (released.Length > 0)
            {
                yield return released;
            }
        }

        if (watcher.IsConfirmedRefusal)
        {
            outcome.WasRefusal = true;
        }
        else
        {
            outcome.WasRefusal = false;
            var remaining = watcher.FlushRemaining();
            if (remaining.Length > 0)
            {
                yield return remaining;
            }
        }

        outcome.FullText = fullText.ToString();
    }
}
