using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (McpClient-mode) implementation of <see cref="IChatUseCase"/>.
/// Session persistence is entirely local — the same <see cref="IChatSessionStore"/>
/// Standalone mode uses (e.g. Looma.Infrastructure.LocalStore's
/// <c>FileChatSessionStore</c>, registered by the composition root
/// alongside this) — only generation calls the remote <c>looma_chat</c>
/// tool. See <c>Looma.Application.UseCases.IChatCompletionUseCase</c>'s
/// doc comment for why sessions never live server-side: they're just
/// conversation text, nothing in them needs Qdrant/Ollama, so there's no
/// reason to make the server stateful for them.
/// </summary>
public sealed class RemoteChatUseCase : IChatUseCase
{
    private readonly McpClient _client;
    private readonly IChatSessionStore _sessionStore;

    public RemoteChatUseCase(McpClient client, IChatSessionStore sessionStore)
    {
        _client = client;
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
        // Fetched before the current user message is appended below — same
        // "genuine prior history only" property as the local ChatUseCase.
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

        // Citations' embeddings are stripped before crossing the wire —
        // same rule as every other payload here (see AnswerTool's doc
        // comment). In practice these are always already null (retrieval
        // never populates DocumentChunk.Embedding), but strip explicitly
        // rather than assume it.
        var wireHistory = session.Messages
            .Select(entry => entry.Citations is null
                ? entry
                : entry with { Citations = entry.Citations.Select(c => c with { Embedding = null }).ToList() })
            .ToList();
        var historyJson = JsonSerializer.Serialize(wireHistory, Wire.Options);

        var arguments = new Dictionary<string, object?>
        {
            ["historyJson"] = historyJson,
            ["message"] = message,
            ["attachmentContext"] = attachmentContext
        };

        var fullAnswer = new StringBuilder();
        IReadOnlyList<DocumentChunk>? finalCitations = null;

        await foreach (var token in RemoteStreamHelper.StreamAsync<AnswerToken>(_client, "looma_chat", arguments, cancellationToken)
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
