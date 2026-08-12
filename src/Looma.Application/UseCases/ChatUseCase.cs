using System.Runtime.CompilerServices;
using System.Text;
using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Local (Standalone-mode) chat orchestration: session persistence via
/// <see cref="IChatSessionStore"/>, generation via
/// <see cref="IChatCompletionUseCase"/>. See
/// <see cref="IChatCompletionUseCase"/>'s doc comment for why generation
/// is split out this way — the same split is what lets
/// <c>Looma.MCP.Client.RemoteChatUseCase</c> reuse this exact
/// session-handling shape while sending generation to a remote
/// <c>looma_chat</c> MCP tool instead of calling
/// <see cref="IChatCompletionUseCase"/> in-process.
///
/// Deliberately does NOT use <c>IAnswerCache</c>. That cache keys strictly
/// on question text/embedding — reusing it here would risk serving a
/// cached answer to what LOOKS like a repeated question but means
/// something entirely different depending on the conversation it's
/// embedded in (e.g. "what about the second one?" asked in two different
/// sessions). Every chat turn is a fresh generation.
///
/// Balanced turns, even on failure: a real, reproduced bug — if generation
/// throws (e.g. Qdrant was briefly unreachable) after the user's message
/// was already persisted, the session was left with a User entry and no
/// matching Assistant reply. The NEXT turn then fed the model two
/// consecutive User messages with no Assistant message between them —
/// confirmed, by reading the actual saved session file, to make even a
/// forceful "answer THIS resolved question" instruction (see
/// <see cref="ChatCompletionUseCase.BuildPrompt"/>) get ignored: the
/// conversation shape itself was out of the alternating User/Assistant
/// structure any chat model is trained on, not just ambiguously worded.
/// <see cref="SendMessageAsync"/> now always appends a synthetic Assistant
/// entry when the turn fails, before rethrowing — the failure is still
/// surfaced to the caller exactly as before, but every future turn's
/// history stays alternating and honestly reflects that no real answer
/// was given (so the grounding rule's "anything you already said" clause
/// can't mistake it for a real fact either).
/// </summary>
public sealed class ChatUseCase : IChatUseCase
{
    private readonly IChatCompletionUseCase _chatCompletionUseCase;
    private readonly IChatSessionStore _sessionStore;

    public ChatUseCase(IChatCompletionUseCase chatCompletionUseCase, IChatSessionStore sessionStore)
    {
        _chatCompletionUseCase = chatCompletionUseCase;
        _sessionStore = sessionStore;
    }

    public Task<ChatSession> StartSessionAsync(CancellationToken cancellationToken = default) =>
        _sessionStore.CreateSessionAsync(cancellationToken);

    public Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default) =>
        _sessionStore.ListSessionsAsync(cancellationToken);

    public Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _sessionStore.GetSessionAsync(sessionId, cancellationToken);

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
        _sessionStore.DeleteSessionAsync(sessionId, cancellationToken);

    public async IAsyncEnumerable<AnswerToken> SendMessageAsync(
        string sessionId,
        string message,
        string? attachmentContext = null,
        string? attachmentLabel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Fetched before the current user message is appended below — this
        // is genuine prior history only, not including the message we're
        // about to send.
        var session = await _sessionStore.GetSessionAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Chat session '{sessionId}' not found.");

        var userMessage = new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.User,
            Text = message,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            AttachmentLabel = attachmentLabel,
            AttachmentContent = attachmentContext
        };
        await _sessionStore.AppendMessageAsync(sessionId, userMessage, cancellationToken).ConfigureAwait(false);

        var fullAnswer = new StringBuilder();
        IReadOnlyList<DocumentChunk>? finalCitations = null;

        // Manually pumping the enumerator (rather than a plain `await
        // foreach`) is what lets a failure be caught here at all — `yield
        // return` can't appear inside a try block that has a catch clause,
        // only try/finally, so the actual try/catch has to wrap just the
        // MoveNextAsync call, with the yield sitting after it, still inside
        // the outer try/finally. The catch block itself has no such
        // restriction, so it can `await` the synthetic-message append and
        // still use a bare `throw;` to preserve the original stack trace.
        // See the class doc comment's "Balanced turns, even on failure"
        // section for why this exists.
        var enumerator = _chatCompletionUseCase
            .CompleteAsync(session.Messages, message, attachmentContext, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                AnswerToken current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                    current = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    // A genuinely cancelled/superseded turn isn't a
                    // "failure" in the sense this fix cares about — the
                    // user moved on, there's nothing useful to record.
                    throw;
                }
                catch (Exception)
                {
                    var failureMessage = new ChatMessageEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        Role = ChatMessageRole.Assistant,
                        Text = "(No answer was generated — this attempt failed before Looma could reply. " +
                               "Nothing here should be treated as an answer to any question.)",
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    await _sessionStore.AppendMessageAsync(sessionId, failureMessage, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!current.IsFinal)
                {
                    fullAnswer.Append(current.Text);
                }
                else
                {
                    finalCitations = current.Citations;
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        var assistantMessage = new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.Assistant,
            Text = fullAnswer.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Citations = finalCitations
        };
        await _sessionStore.AppendMessageAsync(sessionId, assistantMessage, cancellationToken).ConfigureAwait(false);
    }
}
