using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Multi-turn RAG chat — retrieval and grounded generation like
/// <see cref="IAnswerUseCase"/>, but with conversation history in the
/// prompt and both turns persisted to a session. See
/// <see cref="ChatUseCase"/>'s doc comment for why this deliberately
/// doesn't reuse <c>IAnswerCache</c>.
/// </summary>
public interface IChatUseCase
{
    Task<ChatSession> StartSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Newest-updated first.</summary>
    Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);

    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the assistant's reply token by token, citations on the
    /// final token — same shape as <see cref="IAnswerUseCase.AnswerAsync"/>.
    /// Both the user message and the completed assistant message are
    /// persisted to the session as a side effect of enumerating this.
    /// </summary>
    /// <param name="attachmentContext">
    /// Extra grounding material for this turn only — e.g. a caption for an
    /// image the user attached — that the model is told it may answer
    /// from, same as retrieved document excerpts. NOT embedded for
    /// retrieval and NOT persisted as part of the message text (so
    /// reopening the session later won't show it) — deliberately separate
    /// from <paramref name="message"/> so it can't accidentally leak into
    /// what gets searched for or stored. See ChatUseCase's own doc comment
    /// for why this exists as its own parameter instead of being folded
    /// into the message text.
    /// </param>
    IAsyncEnumerable<AnswerToken> SendMessageAsync(
        string sessionId,
        string message,
        string? attachmentContext = null,
        CancellationToken cancellationToken = default);
}
