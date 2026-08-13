using Microsoft.Extensions.AI;

namespace Looma.Application.Internal;

/// <summary>
/// Wraps a single non-streaming <c>IChatClient.GetResponseAsync</c> call
/// with a hard timeout (<see cref="Looma.Application.Configuration.RagOptions.WebSearchGenerationTimeoutSeconds"/>).
///
/// Needed specifically because of the buffered-generation trade-off
/// <see cref="Looma.Application.Configuration.RagOptions.EnableWebSearch"/>
/// makes (see AnswerUseCase's and ChatCompletionUseCase's class doc
/// comments): streaming generation is visibly self-limiting — a runaway or
/// repeating model is obvious to the user immediately, since text keeps
/// printing — but a buffered call shows nothing at all until it completes.
/// A real, reproduced case: local generation fell into a token repetition
/// loop with <c>MaxAnswerTokens</c> left at its default (null / no cap),
/// and with <c>EnableWebSearch</c> on, the desktop app's "thinking"
/// spinner just sat there indefinitely with no visible sign anything was
/// wrong, since there was no partial output to notice.
///
/// A plain <see cref="CancellationTokenSource"/>-based timeout on its own
/// would surface as an <see cref="OperationCanceledException"/> —
/// indistinguishable from the CALLER's own <c>cancellationToken</c> firing
/// (e.g. the user navigating away or sending a new message), which
/// <c>ChatUseCase.SendMessageAsync</c> explicitly treats as "not a real
/// failure, nothing to show the user" (see its doc comment). Throwing
/// <see cref="TimeoutException"/> instead — only when the internal timeout,
/// not the caller's own token, is what actually fired — keeps a genuine
/// hang visible as a real failure message instead of silently vanishing.
/// </summary>
internal static class BufferedGeneration
{
    public static async Task<string> GenerateAsync(
        IChatClient chatClient,
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var response = await chatClient.GetResponseAsync(messages, chatOptions, linkedCts.Token).ConfigureAwait(false);
            return response.Text ?? string.Empty;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Answer generation did not complete within {timeoutSeconds}s. The model may have stalled or " +
                "entered a repetition loop — this is a hard safety limit specific to buffered (non-streaming) " +
                "generation, used when RagOptions.EnableWebSearch is on. See RagOptions.WebSearchGenerationTimeoutSeconds.");
        }
    }
}
