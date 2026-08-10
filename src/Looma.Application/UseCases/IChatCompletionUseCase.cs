using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// The stateless "generate one grounded reply" core of multi-turn chat —
/// retrieval + prompt construction + streaming generation, given an
/// explicit prior-turn history instead of a session id. No session
/// persistence here at all.
///
/// Split out from <see cref="IChatUseCase"/> specifically so
/// Looma.MCP.Server can expose generation without knowing anything about
/// client-side session storage. Chat sessions live entirely on the client
/// (local files, same as Standalone mode via
/// <c>Looma.Infrastructure.LocalStore</c>) — never on the server; only
/// this generation step needs Qdrant/Ollama, which is why it's the one
/// piece that has to cross the wire in McpClient mode (as the
/// <c>looma_chat</c> tool). <see cref="ChatUseCase"/> (local orchestration)
/// and <c>Looma.MCP.Client.RemoteChatUseCase</c> both wrap this — the
/// former by calling it in-process, the latter by calling it remotely —
/// while sharing the exact same session-handling shape.
/// </summary>
public interface IChatCompletionUseCase
{
    /// <param name="history">Prior turns, oldest first — NOT including the new message.</param>
    /// <param name="message">The new user message.</param>
    /// <param name="attachmentContext">
    /// Extra grounding material for this turn only (e.g. an attached
    /// image's caption) — included in the prompt's context block, not
    /// embedded for retrieval. See <see cref="ChatCompletionUseCase"/>'s
    /// own doc comment for why this must live in the context block rather
    /// than the question text.
    /// </param>
    /// <returns>
    /// Streams the assistant's reply token by token, citations on the
    /// final token — same shape as <see cref="IAnswerUseCase.AnswerAsync"/>.
    /// </returns>
    IAsyncEnumerable<AnswerToken> CompleteAsync(
        IReadOnlyList<ChatMessageEntry> history,
        string message,
        string? attachmentContext = null,
        CancellationToken cancellationToken = default);
}
