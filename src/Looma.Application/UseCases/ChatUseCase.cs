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

        await foreach (var token in _chatCompletionUseCase
            .CompleteAsync(session.Messages, message, attachmentContext, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!token.IsFinal)
            {
                fullAnswer.Append(token.Text);
            }
            else
            {
                finalCitations = token.Citations;
            }

            yield return token;
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
