using System.Diagnostics;
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
/// Retrieval-augmented answer generation: embed the question, retrieve the
/// top-K matching chunks, ask the chat model to answer using only that
/// context, and stream the response token by token. Citations (the chunks
/// that were actually retrieved) are attached to the final token rather
/// than duplicated onto every one.
/// </summary>
public sealed class AnswerUseCase : IAnswerUseCase
{
    /// <summary>
    /// Two failure modes had to be balanced against each other here, found
    /// from two different real runs:
    ///
    /// 1. Fabrication — the model filled a genuine gap with plausible
    ///    outside knowledge (invented certification names) instead of
    ///    admitting the context didn't have the answer.
    /// 2. Over-refusal — an earlier, blunter version of this prompt ("even
    ///    if the question is only loosely related to what's in the
    ///    context") overcorrected: asked to summarize a file that was
    ///    clearly and entirely about the topic asked about, the model
    ///    refused anyway, apparently reading "loosely related" as license
    ///    to refuse anything short of an exact phrase match.
    ///
    /// The fix is distinguishing "synthesize freely from what's actually
    /// there" (encouraged) from "invent anything not there" (forbidden) —
    /// not suppressing synthesis altogether. Repeated right before the
    /// question in <see cref="BuildPrompt"/> too, where instructions get
    /// more weight.
    /// </summary>
    private const string SystemPrompt =
        "You are Looma, a local document assistant. Answer using the information in the provided " +
        "context — summarize, combine, or explain what's there freely, even for a broad or " +
        "open-ended question, as long as the context actually contains material relevant to it. " +
        "What you must never do is state a fact, name, number, or claim that isn't actually present " +
        "in the context, no matter how confident you are it's correct. If the context contains " +
        "nothing relevant to the question at all, respond with exactly this sentence and nothing " +
        "else: \"The provided context does not contain this information.\"";

    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly IChatClient _chatClient;
    private readonly IAnswerCache _answerCache;
    private readonly IWebSearchProvider _webSearchProvider;
    private readonly RagOptions _ragOptions;

    public AnswerUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IChatClient chatClient,
        IAnswerCache answerCache,
        IWebSearchProvider webSearchProvider,
        IOptions<RagOptions> ragOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _chatClient = chatClient;
        _answerCache = answerCache;
        _webSearchProvider = webSearchProvider;
        _ragOptions = ragOptions.Value;
    }

    /// <summary>
    /// Set <c>LOOMA_DEBUG_TIMING=1</c> to print per-phase elapsed time for
    /// each <see cref="AnswerAsync"/> call to stderr — added specifically to
    /// track down where time goes on a cache hit, since there's no logging
    /// abstraction elsewhere in this codebase to hang a real one off yet.
    /// </summary>
    private static readonly bool DebugTimingEnabled = Environment.GetEnvironmentVariable("LOOMA_DEBUG_TIMING") == "1";

    public async IAsyncEnumerable<AnswerToken> AnswerAsync(
        string question,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var timer = new PhaseTimer(DebugTimingEnabled);

        // documentsVersion is a cheap staleness signal, not a content hash:
        // any re-index changes the documents collection's chunk count, which
        // invalidates every prior cache entry rather than risking a stale answer.
        var documentsVersion = await _vectorStore.CountAsync(VectorCollection.Documents, cancellationToken).ConfigureAwait(false);
        timer.Mark("count documents (staleness check)");

        // Exact match needs only the literal question — check it before
        // paying for an embedding call, so a repeat question is answered
        // without touching Ollama at all.
        var exactHit = await _answerCache.TryGetExactAsync(question, documentsVersion, cancellationToken).ConfigureAwait(false);
        timer.Mark("exact cache lookup");
        if (exactHit is not null)
        {
            // Already computed — emit it as one burst rather than faking a
            // token-by-token delay for text that's sitting right there.
            yield return new AnswerToken { Text = exactHit.AnswerText, IsFinal = false };
            yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = exactHit.Citations };
            timer.Mark("exact cache hit — done");
            yield break;
        }

        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(question, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        timer.Mark("generate question embedding");

        var semanticHit = await _answerCache.TryGetSemanticAsync(queryEmbedding, documentsVersion, cancellationToken).ConfigureAwait(false);
        timer.Mark("semantic cache lookup");
        if (semanticHit is not null)
        {
            yield return new AnswerToken { Text = semanticHit.AnswerText, IsFinal = false };
            yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = semanticHit.Citations };
            timer.Mark("semantic cache hit — done");
            yield break;
        }

        var citations = new List<DocumentChunk>();
        await foreach (var result in _vectorStore.SearchAsync(
            VectorCollection.Documents, queryEmbedding, _ragOptions.TopK, _ragOptions.MinRelevanceScore, cancellationToken)
            .ConfigureAwait(false))
        {
            citations.Add(new DocumentChunk
            {
                Id = result.Id,
                SourceId = result.Metadata.SourcePath,
                Content = result.Content ?? string.Empty,
                Metadata = result.Metadata
            });
        }
        timer.Mark("retrieve context chunks");

        // See WebSearchFallback's doc comment — a no-op unless local
        // retrieval above found nothing AND RagOptions.EnableWebSearch is
        // on. Uses the literal question, same as retrieval above (no query
        // reformulation exists at this layer — that's chat-only, see
        // ChatCompletionUseCase).
        citations = await WebSearchFallback
            .AugmentIfEmptyAsync(citations, question, _ragOptions, _webSearchProvider, cancellationToken)
            .ConfigureAwait(false);
        timer.Mark("web search fallback");

        var messages = BuildPrompt(question, citations);

        // Low temperature by default: this is grounded RAG Q&A, not creative
        // writing — a lower temperature measurably reduces the model's
        // tendency to elaborate beyond the given context. MaxOutputTokens is
        // only set if explicitly configured — null means no cap, letting the
        // model answer as long as it actually needs to.
        var chatOptions = new ChatOptions { Temperature = _ragOptions.AnswerTemperature };
        if (_ragOptions.MaxAnswerTokens is { } maxTokens)
        {
            chatOptions.MaxOutputTokens = maxTokens;
        }

        var fullAnswer = new StringBuilder();
        var firstTokenMarked = false;
        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                if (!firstTokenMarked)
                {
                    timer.Mark("time to first token");
                    firstTokenMarked = true;
                }

                fullAnswer.Append(update.Text);
                yield return new AnswerToken { Text = update.Text, IsFinal = false };
            }
        }
        timer.Mark("chat generation complete");

        yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = citations };

        await _answerCache.StoreAsync(question, queryEmbedding, fullAnswer.ToString(), citations, documentsVersion, cancellationToken)
            .ConfigureAwait(false);
        timer.Mark("store cache");
    }

    /// <summary>Tiny stopwatch-based phase logger — see <see cref="DebugTimingEnabled"/>.</summary>
    private sealed class PhaseTimer
    {
        private readonly bool _enabled;
        private readonly Stopwatch _stopwatch;
        private long _lastElapsedMs;

        public PhaseTimer(bool enabled)
        {
            _enabled = enabled;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Mark(string phase)
        {
            if (!_enabled)
            {
                return;
            }

            var elapsed = _stopwatch.ElapsedMilliseconds;
            Console.Error.WriteLine($"[timing] {phase}: {elapsed - _lastElapsedMs}ms (total {elapsed}ms)");
            _lastElapsedMs = elapsed;
        }
    }

    private static List<ChatMessage> BuildPrompt(string question, IReadOnlyList<DocumentChunk> citations)
    {
        var context = new StringBuilder();
        if (citations.Count == 0)
        {
            context.Append("(no matching context was found in the index)");
        }
        else
        {
            for (var i = 0; i < citations.Count; i++)
            {
                context.Append('[').Append(i + 1).Append("] (").Append(citations[i].SourceId).Append(")\n")
                    .Append(citations[i].Content).Append("\n\n");
            }
        }

        var userMessage = $"Context:\n{context}\n" +
                           $"Answer from the context above — summarizing or combining what's there " +
                           $"is fine, but don't add anything that isn't actually in it. If none of " +
                           $"it is relevant to the question, reply with exactly: " +
                           $"\"{GroundedAnswer.NoAnswerSentence}\"\n\n" +
                           $"Question: {question}";

        return
        [
            new ChatMessage(ChatRole.System, SystemPrompt),
            new ChatMessage(ChatRole.User, userMessage)
        ];
    }
}
