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
///
/// Web search fallback: when <see cref="RagOptions.EnableWebSearch"/> is
/// on, a local-only answer that comes back as a refusal
/// (<see cref="GroundedAnswer.NoAnswerSentence"/>) triggers a web search
/// and a second generation pass using the web results instead. Deciding
/// this needs the complete answer text — there's no way to know mid-stream
/// whether the model is about to refuse — but that no longer means
/// buffering the whole thing before showing anything: <c>RefusalPrefixWatcher</c>
/// buffers only enough streamed text to tell whether it could still become
/// the exact refusal sentence, releasing everything else live as it
/// arrives. In practice this means a real answer (the common case) streams
/// close to normally; only an actual refusal pays a brief wait, bounded by
/// the refusal sentence's own short, fixed length — not the whole answer.
///
/// <see cref="RetrievalConfidence"/> (from citation count, BEFORE
/// generation starts) adds two further optimizations on top of that:
/// zero citations skips local generation entirely and goes straight to a
/// web search (nothing local to even try), and a thin candidate pool
/// starts the web search speculatively, in parallel with local generation,
/// so if it does turn out to be needed the results are likely already
/// back. Neither of these decides whether the fallback fires — that's
/// still always <c>RefusalPrefixWatcher</c>'s job, per <c>WebSearchFallback</c>'s
/// doc comment on why a citation-based trigger was tried once already and
/// found unreliable (adaptive thresholding routinely leaves a few weak
/// citations around even for genuinely unanswerable questions).
///
/// <see cref="AnswerToken.IsStatus"/> tokens narrate the stage transitions
/// this creates ("Generating answer...", "No local match found — searching
/// the web...") — real progress, not a fake placeholder, since a consumer
/// can no longer assume the FIRST non-final token it sees is answer
/// content the moment <c>EnableWebSearch</c> is on.
///
/// With <c>EnableWebSearch</c> off (the default), none of this applies —
/// this still streams exactly as before that feature existed.
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
        ThinkingMode thinkingMode = ThinkingMode.Ask,
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

        // See RagOptions.EnableHybridSearch's doc comment — MinRelevanceScore
        // is ignored server-side once a query is hybrid (dense+BM25 fused
        // via RRF), harmless to still pass. Collected into a list first
        // (rather than mapped to DocumentChunk directly, as before) because
        // the hybrid path needs RagRetrieval.ApplyHybridRelevanceFilter
        // applied afterward — see RagOptions.HybridRelevanceRatio's doc
        // comment for why raw TopK alone let unrelated chunks pad the
        // result set.
        var rawResults = new List<VectorSearchResult>();
        await foreach (var result in _vectorStore.SearchAsync(
            VectorCollection.Documents,
            queryEmbedding,
            _ragOptions.TopK,
            _ragOptions.MinRelevanceScore,
            _ragOptions.EnableHybridSearch ? question : null,
            cancellationToken)
            .ConfigureAwait(false))
        {
            rawResults.Add(result);
        }

        var filteredResults = _ragOptions.EnableHybridSearch
            ? RagRetrieval.ApplyHybridRelevanceFilter(rawResults, _ragOptions)
            : rawResults;

        var citations = filteredResults.Select(result => new DocumentChunk
        {
            Id = result.Id,
            SourceId = result.Metadata.SourcePath,
            Content = result.Content ?? string.Empty,
            Metadata = result.Metadata
        }).ToList();
        timer.Mark("retrieve context chunks");

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

        // See ThinkingModeChatOption's doc comment — this is the one place
        // per call that sets it; every generation call below reuses this
        // same chatOptions instance (including the web-fallback retry), so
        // the caller's choice applies consistently across all of them.
        chatOptions.AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [ThinkingModeChatOption.Key] = thinkingMode
        };

        string fullAnswerText;

        if (!_ragOptions.EnableWebSearch)
        {
            // Default path — streams exactly as before this feature existed.
            var messages = BuildPrompt(question, citations);
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
            fullAnswerText = fullAnswer.ToString();
        }
        else
        {
            // See the class doc comment's "Web search fallback" section —
            // citation count only ever affects presentation/scheduling
            // here, never whether the fallback actually fires.
            var confidence = RetrievalConfidenceClassifier.Classify(citations.Count);

            if (confidence == RetrievalConfidence.Empty)
            {
                yield return new AnswerToken
                {
                    Text = "No local match found — searching the web...", IsFinal = false, IsStatus = true
                };

                var webCitations = await WebSearchFallback
                    .TryWebSearchAsync(question, _ragOptions, _webSearchProvider, cancellationToken)
                    .ConfigureAwait(false);
                timer.Mark("web search fallback");

                if (webCitations.Count > 0)
                {
                    citations = webCitations;
                }

                // Still generate even if the web search also found nothing
                // — there's no further fallback left to try, so whatever
                // the model says now (likely the refusal sentence again,
                // honestly this time) is the final answer.
                var messages = BuildPrompt(question, citations);
                var fullAnswer = new StringBuilder();
                await foreach (var chunk in IdleTimeoutStreaming
                    .StreamAsync(_chatClient, messages, chatOptions, _ragOptions.WebSearchGenerationTimeoutSeconds, cancellationToken)
                    .ConfigureAwait(false))
                {
                    fullAnswer.Append(chunk);
                    yield return new AnswerToken { Text = chunk, IsFinal = false };
                }
                timer.Mark("chat generation complete");
                fullAnswerText = fullAnswer.ToString();
            }
            else
            {
                // A thin pool (Low): start the web search now, in parallel
                // with local generation below — it's just an HTTP search,
                // not a second full model call, so this is cheap even when
                // it turns out not to be needed. WebSearchFallback never
                // throws except on a genuine cancellation (see its doc
                // comment), and a canceled-but-unawaited task doesn't
                // trigger .NET's unobserved-exception handling, so no
                // explicit continuation/observation is needed if this ends
                // up discarded below.
                var speculativeWebSearchTask = confidence == RetrievalConfidence.Low
                    ? WebSearchFallback.TryWebSearchAsync(question, _ragOptions, _webSearchProvider, cancellationToken)
                    : null;

                yield return new AnswerToken { Text = "Generating answer...", IsFinal = false, IsStatus = true };

                var messages = BuildPrompt(question, citations);
                var outcome = new RefusalAwareGenerationOutcome();
                await foreach (var chunk in RefusalAwareGeneration
                    .StreamAsync(_chatClient, messages, chatOptions, _ragOptions.WebSearchGenerationTimeoutSeconds, outcome, cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return new AnswerToken { Text = chunk, IsFinal = false };
                }
                timer.Mark("chat generation complete");
                fullAnswerText = outcome.FullText;

                if (outcome.WasRefusal)
                {
                    yield return new AnswerToken
                    {
                        Text = "No local match found — searching the web...", IsFinal = false, IsStatus = true
                    };

                    var webCitations = speculativeWebSearchTask is not null
                        ? await speculativeWebSearchTask.ConfigureAwait(false)
                        : await WebSearchFallback.TryWebSearchAsync(question, _ragOptions, _webSearchProvider, cancellationToken).ConfigureAwait(false);
                    timer.Mark("web search fallback");

                    if (webCitations.Count > 0)
                    {
                        citations = webCitations;
                        var retryMessages = BuildPrompt(question, citations);
                        var retryAnswer = new StringBuilder();
                        await foreach (var chunk in IdleTimeoutStreaming
                            .StreamAsync(_chatClient, retryMessages, chatOptions, _ragOptions.WebSearchGenerationTimeoutSeconds, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            retryAnswer.Append(chunk);
                            yield return new AnswerToken { Text = chunk, IsFinal = false };
                        }
                        timer.Mark("web-augmented regeneration complete");
                        fullAnswerText = retryAnswer.ToString();
                    }
                    else
                    {
                        // No web results either — RefusalPrefixWatcher held
                        // the refusal sentence back the whole time, so it
                        // was never actually shown; surface it now as the
                        // honest final answer.
                        yield return new AnswerToken { Text = fullAnswerText, IsFinal = false };
                    }
                }
            }
        }

        yield return new AnswerToken { Text = string.Empty, IsFinal = true, Citations = citations };

        await _answerCache.StoreAsync(question, queryEmbedding, fullAnswerText, citations, documentsVersion, cancellationToken)
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
