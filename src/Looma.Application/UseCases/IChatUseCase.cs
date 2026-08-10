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
    /// Extra grounding material for this turn — e.g. a caption for an
    /// image the user attached — that the model is told it may answer
    /// from, same as retrieved document excerpts. NOT embedded for
    /// retrieval and NOT added to the global document index — but IS
    /// persisted (on <see cref="ChatMessageEntry.AttachmentContent"/>) so
    /// LATER turns in THIS SAME session can still draw on it, re-surfaced
    /// by <c>ChatCompletionUseCase</c>'s sticky-attachment handling.
    /// Deliberately a separate parameter from <paramref name="message"/>
    /// so it can't accidentally leak into what gets searched for or into
    /// the question text itself. See ChatUseCase's own doc comment for why
    /// it's split out this way.
    /// </param>
    /// <param name="attachmentLabel">
    /// Just the attached file's name (e.g. "invoice.pdf"), if any —
    /// persisted alongside <paramref name="attachmentContext"/>
    /// (on <see cref="ChatMessageEntry.AttachmentLabel"/>) purely for
    /// display: a reopened session shows "📎 invoice.pdf" next to the turn
    /// that used it, and later turns' sticky-attachment context block
    /// labels this material by name.
    /// </param>
    IAsyncEnumerable<AnswerToken> SendMessageAsync(
        string sessionId,
        string message,
        string? attachmentContext = null,
        string? attachmentLabel = null,
        CancellationToken cancellationToken = default);
}
