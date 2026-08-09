using System.Runtime.CompilerServices;
using System.Text;
using Looma.Application.Configuration;
using Looma.Application.Internal;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Looma.Application.UseCases;

/// <summary>
/// Multi-turn chat: the same retrieval + grounded-answer generation as
/// <see cref="AnswerUseCase"/>, except (a) prior turns are included in the
/// prompt so follow-ups ("what about the other one?") have context, and
/// (b) both the question and the answer are persisted via
/// <see cref="IChatSessionStore"/>.
///
/// Deliberately does NOT use <c>IAnswerCache</c>. That cache keys strictly
/// on question text/embedding — reusing it here would risk serving a
/// cached answer to what LOOKS like a repeated question but means
/// something entirely different depending on the conversation it's
/// embedded in (e.g. "what about the second one?" asked in two different
/// sessions). Every chat turn is a fresh generation.
///
/// Known limitation: retrieval is keyed on the latest message alone, not
/// the full conversation — a follow-up like "what about the other one?"
/// may retrieve poorly since the retrieval query itself has no context
/// beyond that one message. Query reformulation (condensing history +
/// follow-up into a self-contained search query before embedding) would
/// fix this; not implemented yet.
/// </summary>
public sealed class ChatUseCase : IChatUseCase
{
    private const string SystemPrompt =
        "You are Looma, a local document assistant having a multi-turn conversation. Answer using " +
        "the information in the provided context — this may include excerpts retrieved from indexed " +
        "documents, and/or a description of an image the user attached to this message; both are " +
        "equally valid material to answer from. Summarize, combine, or explain what's there freely, " +
        "even for a broad or open-ended question, as long as the context actually contains material " +
        "relevant to it. What you must never do is state a fact, name, number, or claim that isn't " +
        "actually present in the context, no matter how confident you are it's correct. If the " +
        "context contains nothing relevant to the current question, respond with exactly this " +
        "sentence and nothing else: \"The provided context does not contain this information.\" Use " +
        "the prior conversation turns only to understand what the user is asking about (pronouns, " +
        "follow-ups) — never as a source of facts beyond what's in the context below.";

    private const string NoAnswerSentence = "The provided context does not contain this information.";

    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IChatClient _chatClient;
    private readonly IChatSessionStore _sessionStore;
    private readonly RagOptions _ragOptions;

    public ChatUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IChatClient chatClient,
        IChatSessionStore sessionStore,
        IOptions<RagOptions> ragOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _chatClient = chatClient;
        _sessionStore = sessionStore;
        _ragOptions = ragOptions.Value;
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
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await _sessionStore.AppendMessageAsync(sessionId, userMessage, cancellationToken).ConfigureAwait(false);

        var queryEmbedding = await _embeddingGenerator
            .GenerateVectorAsync(message, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var citations = await RagRetrieval
            .RetrieveCitationsAsync(_vectorStore, queryEmbedding, _ragOptions.TopK, _ragOptions.MinRelevanceScore, cancellationToken)
            .ConfigureAwait(false);

        var promptMessages = BuildPrompt(session.Messages, message, citations, attachmentContext);

        var chatOptions = new ChatOptions { Temperature = _ragOptions.AnswerTemperature };
        if (_ragOptions.MaxAnswerTokens is { } maxTokens)
        {
            chatOptions.MaxOutputTokens = maxTokens;
        }

        var fullAnswer = new StringBuilder();
        await foreach (var update in _chatClient.GetStreamingResponseAsync(promptMessages, chatOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                fullAnswer.Append(update.Text);
                yield return new AnswerToken { Text = update.Text, IsFinal = false };
            }
        }

        yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = citations };

        var assistantMessage = new ChatMessageEntry
        {
            Id = Guid.NewGuid().ToString(),
            Role = ChatMessageRole.Assistant,
            Text = fullAnswer.ToString(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Citations = citations
        };
        await _sessionStore.AppendMessageAsync(sessionId, assistantMessage, cancellationToken).ConfigureAwait(false);
    }

    private static List<ChatMessage> BuildPrompt(
        IReadOnlyList<ChatMessageEntry> priorMessages,
        string currentMessage,
        IReadOnlyList<DocumentChunk> citations,
        string? attachmentContext)
    {
        var context = new StringBuilder();

        // Attached-image caption first, clearly labeled, ahead of the
        // retrieved document excerpts — this is what actually fixes "ask
        // about it live": it lives in the same context block the system
        // prompt grants permission to answer from, instead of being
        // smuggled into the question text where the grounding rule below
        // would (correctly, by its own logic) refuse to use it.
        if (!string.IsNullOrWhiteSpace(attachmentContext))
        {
            context.Append("Attached image: ").Append(attachmentContext).Append("\n\n");
        }

        if (citations.Count == 0)
        {
            if (context.Length == 0)
            {
                context.Append("(no matching context was found in the index)");
            }
        }
        else
        {
            for (var i = 0; i < citations.Count; i++)
            {
                context.Append('[').Append(i + 1).Append("] (").Append(citations[i].SourceId).Append(")\n")
                    .Append(citations[i].Content).Append("\n\n");
            }
        }

        var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };

        // Genuine prior turns, verbatim — gives the model conversational
        // context (pronouns, follow-ups) without re-sending past citations
        // (Microsoft.Extensions.AI's ChatMessage has nowhere clean to carry
        // them, and the model doesn't need earlier citations to answer the
        // current question — only the current context block below).
        foreach (var entry in priorMessages)
        {
            messages.Add(new ChatMessage(
                entry.Role == ChatMessageRole.User ? ChatRole.User : ChatRole.Assistant,
                entry.Text));
        }

        var userMessage = $"Context:\n{context}\n" +
                           $"Answer from the context above — summarizing or combining what's there " +
                           $"is fine, but don't add anything that isn't actually in it. If none of " +
                           $"it is relevant to the current question, reply with exactly: " +
                           $"\"{NoAnswerSentence}\"\n\n" +
                           $"Question: {currentMessage}";

        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        return messages;
    }
}
