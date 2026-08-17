using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Looma.Application.Internal;

/// <summary>
/// Wraps <c>IChatClient.GetStreamingResponseAsync</c> with an IDLE timeout —
/// reset on every chunk received, not a single total-time budget — and
/// yields each non-empty text chunk as it arrives. Replaces
/// <c>BufferedGeneration</c> now that
/// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>'s
/// path streams live instead of buffering the whole answer before showing
/// anything (see <c>RefusalPrefixWatcher</c> for how the refusal-detection
/// decision that used to require buffering now works without it).
///
/// A total-time budget doesn't fit streaming: a slow-but-still-producing
/// answer shouldn't be killed just for taking a while — the whole point of
/// streaming is that visible, ongoing progress IS the signal something's
/// still working. What actually indicates a real stall or repetition loop
/// that's stopped producing anything NEW is a gap between chunks, so that's
/// what this measures instead.
///
/// Same reasoning as <c>BufferedGeneration</c> for why a genuine idle
/// timeout has to throw <see cref="TimeoutException"/> rather than let a
/// plain <see cref="OperationCanceledException"/> surface: the CALLER's own
/// <c>cancellationToken</c> firing (navigating away, sending a new message)
/// must stay indistinguishable from a normal cancellation —
/// <c>ChatUseCase.SendMessageAsync</c> explicitly treats that as "not a real
/// failure." Two DIFFERENT <see cref="CancellationTokenSource"/> roles are
/// at play here, deliberately kept separate: the idle TIMER itself
/// (<c>idleTimeoutTask</c>) is a plain, unlinked <see cref="Task.Delay(TimeSpan)"/>
/// racing against the next chunk — never linked to any token — so the
/// caller's own cancellation is only ever observed through
/// <c>moveNextTask</c> itself, never confused with an idle timeout by
/// <see cref="Task.WhenAny(Task,Task)"/> picking a winner. A SEPARATE,
/// internal-only linked token (see <c>StreamAsync</c>'s own comment) exists
/// purely so an idle timeout, once decided, can actually cancel the
/// abandoned underlying call before disposing it — unrelated to which
/// winner fired.
/// </summary>
internal static class IdleTimeoutStreaming
{
    public static async IAsyncEnumerable<string> StreamAsync(
        IChatClient chatClient,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        int idleTimeoutSeconds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // A dedicated, internally-cancellable token (linked to the
        // caller's) — separate from cancellationToken specifically so that
        // when OUR idle timer fires, something exists that WE'RE allowed to
        // cancel. A real, reproduced bug this fixes: without this, the
        // underlying MoveNextAsync() call that was still in flight when the
        // idle timer won stays outstanding forever (nothing ever tells it
        // to stop), and disposing an async-iterator enumerator while a
        // MoveNextAsync() call on it hasn't completed yet throws
        // NotSupportedException — meaning the `finally` block below could
        // never actually clean up, masking the real TimeoutException behind
        // a confusing, unrelated one. Caught by this class's own unit
        // tests, not guessed.
        using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var enumerator = chatClient
            .GetStreamingResponseAsync(messages, chatOptions, internalCts.Token)
            .GetAsyncEnumerator(internalCts.Token);

        try
        {
            while (true)
            {
                var moveNextTask = enumerator.MoveNextAsync().AsTask();

                // A fresh, unlinked timer every iteration — this is the
                // "reset on every chunk" behavior; a slow FIRST token (cold
                // model load — a real, previously observed case, easily
                // 60s+ on CPU-only hardware) gets the same grace period as
                // every gap after it, not a shorter one.
                var idleTimeoutTask = Task.Delay(TimeSpan.FromSeconds(idleTimeoutSeconds));

                var winner = await Task.WhenAny(moveNextTask, idleTimeoutTask).ConfigureAwait(false);

                if (winner == idleTimeoutTask)
                {
                    // Cancel the underlying call FIRST, then wait for the
                    // now-canceled moveNextTask to actually finish, before
                    // this method's `finally` disposes the enumerator — see
                    // this method's opening comment for why disposing while
                    // it's still outstanding isn't safe. Whatever
                    // moveNextTask completes with (typically
                    // OperationCanceledException, since we just caused
                    // that) is expected and deliberately discarded — the
                    // TimeoutException below is the one that actually
                    // matters to the caller.
                    internalCts.Cancel();
                    try
                    {
                        await moveNextTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Discarded — see comment above.
                    }

                    throw new TimeoutException(
                        $"No new output for {idleTimeoutSeconds}s — the model may have stalled or entered a " +
                        "repetition loop that stopped producing new text (or, on the very first request after " +
                        "starting Ollama, a slow cold model load). This is a hard safety limit on streaming " +
                        "generation used when RagOptions.EnableWebSearch is on. See " +
                        "RagOptions.WebSearchGenerationTimeoutSeconds.");
                }

                // Not the idle timer — awaiting the already-completed
                // moveNextTask here just retrieves its result (or rethrows
                // whatever it actually failed with, including the caller's
                // own OperationCanceledException, unaltered).
                if (!await moveNextTask.ConfigureAwait(false))
                {
                    yield break;
                }

                var update = enumerator.Current;
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
