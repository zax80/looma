using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>
/// Persists multi-turn chat sessions. Implementations must persist across
/// process runs — same reasoning as <see cref="IAnswerCache"/>: a GUI app
/// is long-lived within one run, but the user expects chat history to
/// survive an app restart.
/// </summary>
public interface IChatSessionStore
{
    Task<ChatSession> CreateSessionAsync(CancellationToken cancellationToken = default);

    Task<ChatSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Newest-updated first.</summary>
    Task<IReadOnlyList<ChatSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends one message and bumps <see cref="ChatSession.UpdatedAtUtc"/>.
    /// The session's <see cref="ChatSession.Title"/> is derived from the
    /// first user message the first time one is appended — callers never
    /// set it directly.
    /// </summary>
    Task AppendMessageAsync(string sessionId, ChatMessageEntry message, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
