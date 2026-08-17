namespace Looma.Application.Configuration;

/// <summary>Binds to the <c>RAG</c> section of config.json.</summary>
public sealed class RagOptions
{
    public const string SectionName = "RAG";

    /// <summary>
    /// Chunk size and overlap, in characters — not tokens. Exact chunking
    /// parameters are called out as an open question in
    /// docs/looma-project-brief.md ("proposed default ~400 tokens / 50
    /// overlap — confirm or tune"); a real tokenizer is a separate,
    /// unverified dependency, so this milestone treats config.json's
    /// ChunkSize/ChunkOverlap as character counts. Revisit once a specific
    /// tokenizer is chosen.
    ///
    /// Treated as an upper bound on whole lines packed together, not a raw
    /// character window — see <c>TextChunker</c> for why (splitting a line
    /// mid-word corrupted real content: "IVAN SPAHIYSKI" became "N
    /// SPAHIYSKI" at a chunk boundary).
    ///
    /// Raised from the original 400/50 (~100 tokens/chunk) — that was far
    /// below what either model in the pipeline can actually use:
    /// nomic-embed-text embeds up to 8192 tokens per input, and the chat
    /// model's own context (also 8192 tokens, see BaseModel.ContextSize)
    /// has to hold TopK citations plus conversation history plus the
    /// system prompt together, not one citation alone. 800/100 (~200
    /// tokens/chunk, same 12.5% overlap ratio as before) means each
    /// retrieved chunk carries more complete context — directly relevant
    /// to bugs like a fact getting split across a chunk boundary — while 5
    /// citations at this size (~1000 tokens) still leaves most of the
    /// context window for everything else. Like MinRelevanceScore, this is
    /// a reasoned starting point, not a measured-optimal one — use `looma
    /// search`/`looma answer` on real documents to validate before tuning
    /// further, don't guess past this blind.
    /// </summary>
    public int ChunkSize { get; set; } = 800;
    public int ChunkOverlap { get; set; } = 100;

    public int TopK { get; set; } = 5;

    /// <summary>
    /// Cosine similarity threshold for a chunk to be used as context.
    /// Calibrated against real nomic-embed-text scores, not guessed: for a
    /// short natural-language question against a genuinely relevant chunk
    /// (e.g. "what certificates do I have" against the chunk listing them),
    /// scores landed around 0.55-0.67; clearly irrelevant chunks from an
    /// unrelated document topped out around 0.44-0.50 for the same queries.
    /// The original default of 0.7 was above every true positive observed —
    /// nothing ever passed it, so `answer` always saw empty context. 0.55
    /// sits in the gap between those two clusters with margin on both
    /// sides. Use `looma search "<query>"` to see real scores before
    /// tuning this further — don't guess a new value blind.
    /// </summary>
    public float MinRelevanceScore { get; set; } = 0.55f;

    /// <summary>
    /// Optional safety net against a runaway/looping generation — NOT a
    /// speed optimization, and not set by default. Capping this trades
    /// completeness for a worst-case time bound: a real answer that needed
    /// more tokens gets truncated instead, which is a worse outcome, not a
    /// faster correct one. Streaming (see AnswerUseCase/AnswerCommand) is
    /// what actually makes answers feel responsive — tokens print as
    /// they're generated, not after the full response completes. Leave
    /// null unless you've actually hit runaway generation.
    /// </summary>
    public int? MaxAnswerTokens { get; set; }

    /// <summary>
    /// Chat sampling temperature for <c>answer</c>. Low by default (not the
    /// provider's usual ~0.8 default) — this is grounded RAG Q&A, where the
    /// model should stick closely to the provided context, not open-ended
    /// creative generation. A real run showed the model filling a gap with
    /// plausible-sounding outside knowledge instead of admitting the
    /// context didn't have the answer; a lower temperature is one part of
    /// the fix (see the prompt wording in <c>AnswerUseCase</c> for the rest).
    /// Raise this if answers feel too terse/repetitive for your use case.
    /// </summary>
    public float AnswerTemperature { get; set; } = 0.1f;

    /// <summary>
    /// Chat only, same scope as <see cref="EnableQueryReformulation"/> —
    /// see <c>RagRetrieval.RetrieveCitationsAsync</c>. Instead of a single
    /// fixed <see cref="MinRelevanceScore"/> cutoff applied the same way to
    /// every query, this keeps whichever of the TopK candidates score
    /// within <see cref="AdaptiveThresholdMargin"/> of the BEST match for
    /// THIS query, rather than a flat absolute floor. The problem a flat
    /// floor has: it's calibrated against an average case, but real queries
    /// vary — an easy query might have five chunks all scoring 0.75+ (a
    /// flat floor wrongly excludes none of them, fine), while a harder,
    /// terser, or more obliquely-phrased query might have its best genuine
    /// match score only 0.58, with the next-best merely-related chunk right
    /// behind at 0.56 — a flat floor of 0.55 keeps both even though the
    /// second is clearly weaker relative to the first for that query.
    /// Adaptive thresholding fixes the second case without touching the
    /// first: it always keeps the top match (whatever its absolute score,
    /// down to <see cref="AdaptiveFloorScore"/>) and only keeps runners-up
    /// that are genuinely close to it, not just "above some fixed line."
    /// No extra retrieval or LLM call — same TopK candidates already
    /// fetched, just filtered smarter. Default on since it can only prune
    /// what a flat floor would have kept, never add anything a flat floor
    /// would have excluded.
    /// </summary>
    public bool EnableAdaptiveThreshold { get; set; } = true;

    /// <summary>
    /// The floor <see cref="EnableAdaptiveThreshold"/> searches against
    /// instead of <see cref="MinRelevanceScore"/> — deliberately lower, so
    /// a genuinely relevant chunk on a hard query (real scores as low as
    /// 0.55-0.58 were observed for true positives — see
    /// <see cref="MinRelevanceScore"/>'s doc comment) still gets retrieved
    /// as a CANDIDATE even if it would've been excluded outright by the
    /// stricter flat floor; <see cref="AdaptiveThresholdMargin"/> is what
    /// then decides whether it's actually kept, based on the rest of that
    /// query's own results. Set above the observed false-positive band
    /// (0.44-0.50) so this never widens the candidate pool into chunks
    /// already known to be noise. Same "reasoned starting point, not
    /// measured-optimal" caveat as <see cref="MinRelevanceScore"/> — use
    /// <c>looma search "&lt;query&gt;" --min-score 0</c> to see real score
    /// spreads before tuning either value further.
    /// </summary>
    public float AdaptiveFloorScore { get; set; } = 0.40f;

    /// <summary>
    /// How far below the best-scoring candidate a runner-up can fall and
    /// still count as "genuinely close" — see
    /// <see cref="EnableAdaptiveThreshold"/>. 0.15 is a starting point, not
    /// a measured-optimal one: wide enough that a cluster of similarly-
    /// worded chunks about the same fact (a common real shape — e.g. a
    /// fact split across an original chunk and its overlap region) doesn't
    /// get needlessly pruned down to just one, narrow enough that a
    /// clearly-secondary match well below the best one still gets cut.
    /// </summary>
    public float AdaptiveThresholdMargin { get; set; } = 0.15f;

    /// <summary>
    /// Whether chat turns after the first rewrite the follow-up into a
    /// standalone search query — using recent conversation history to
    /// resolve pronouns/implicit references ("it", "that", "the other
    /// one") into their actual subject — before embedding it for
    /// retrieval. See <c>ChatCompletionUseCase.ReformulateQueryAsync</c>.
    /// Default on: this is what actually fixes retrieval being blind to
    /// what the conversation is about, the general case of a real bug hit
    /// in testing (sticky attachment memory fixed the attachment-specific
    /// version of this; this fixes it for ordinary document retrieval
    /// too — "who's the author?" retrieving an unrelated document because
    /// the raw follow-up alone has nothing to search on). The real cost is
    /// latency, not quality: one more non-streaming LLM call, sequenced
    /// BEFORE the visible streaming answer even starts, on every turn
    /// after the first. Turn off if that pause matters more than
    /// occasionally poor multi-turn retrieval on your hardware.
    /// </summary>
    public bool EnableQueryReformulation { get; set; } = true;

    /// <summary>
    /// Whether a local-only answer that comes back as a refusal
    /// (<see cref="Looma.Application.GroundedAnswer.NoAnswerSentence"/>)
    /// triggers a web search and a second generation pass using the web
    /// results (see <c>WebSearchFallback</c>/
    /// <see cref="Looma.Core.Abstractions.IWebSearchProvider"/>), instead of
    /// answering "the provided context does not contain this information"
    /// outright. Off by default — its own separate toggle from every other
    /// RAG behavior here, since it makes outbound network calls (to
    /// DuckDuckGo's HTML search page, no API key or separate service to
    /// run — see docs/config-reference.md's WebSearch section) that an
    /// install never explicitly asked for shouldn't happen silently.
    ///
    /// The trigger is "the answer was a refusal" — NOT "zero local
    /// citations," which is what this originally checked and was a real bug
    /// caught in live testing: with <see cref="EnableAdaptiveThreshold"/> on,
    /// local retrieval routinely returns SOME citations for almost any query
    /// (a handful of weakly-scoring, ultimately irrelevant chunks is the
    /// common case), so "zero citations" essentially never happened and the
    /// fallback almost never fired even when the model correctly refused to
    /// use what it had. <c>RetrievalConfidenceClassifier</c> DOES also look
    /// at citation count, but only to decide presentation/scheduling (skip a
    /// near-certainly-futile local attempt when there are truly zero
    /// citations; speculatively start the web search early, in parallel,
    /// when there are only one or two) — it is never what decides whether
    /// the final answer is accepted, which is still always the refusal
    /// check, exactly as the live-tested fix above requires.
    ///
    /// Originally, deciding this required buffering the whole answer before
    /// showing anything (a real, live-tested UX cost — see git history for
    /// <c>BufferedGeneration</c>). It doesn't anymore:
    /// <c>RefusalPrefixWatcher</c> buffers only enough streamed text to tell
    /// whether it could still become the exact refusal sentence, flushing
    /// and streaming live the moment it clearly can't — see AnswerUseCase's
    /// and ChatCompletionUseCase's class doc comments for the "Web search
    /// fallback" section each has.
    /// </summary>
    public bool EnableWebSearch { get; set; } = false;

    /// <summary>
    /// How many web results to fold into the context when
    /// <see cref="EnableWebSearch"/> triggers. Deliberately smaller than
    /// <see cref="TopK"/> — a web fallback is meant to cover a gap in the
    /// local index, not compete with it on breadth, and each result adds a
    /// full page's title+snippet to the prompt with none of local
    /// retrieval's relevance-score filtering to thin it out.
    /// </summary>
    public int WebSearchMaxResults { get; set; } = 3;

    /// <summary>
    /// IDLE timeout, in seconds — resets on every streamed chunk, not a
    /// total-time budget — applied to generation calls made when
    /// <see cref="EnableWebSearch"/> is on. See <c>IdleTimeoutStreaming</c>
    /// for the mechanism and full reasoning.
    ///
    /// Was originally a total-wall-clock timeout on a fully buffered
    /// (non-streaming) call, back when <see cref="EnableWebSearch"/> traded
    /// away live streaming entirely to make the refusal-retry decision (see
    /// that property's doc comment for why that trade existed and how
    /// <c>RefusalPrefixWatcher</c> now avoids it). A total-time budget
    /// doesn't fit a live stream — a slow-but-still-producing answer
    /// shouldn't be killed just for taking a while — so this now measures
    /// gaps between chunks instead, which is what actually indicates a
    /// stalled or repetition-looping model. 60s is a generous per-gap
    /// default, including for a slow cold model load on the very first
    /// request after starting Ollama (a real, previously observed case);
    /// lower it if you'd rather fail fast once generation is actually
    /// producing nothing new.
    /// </summary>
    public int WebSearchGenerationTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Whether local retrieval combines vector similarity with keyword
    /// (BM25) matching, fused via Qdrant's Query API (Reciprocal Rank
    /// Fusion), instead of vector similarity alone. Off by default — this
    /// is a real, breaking Qdrant schema change (a named dense + named
    /// sparse vector, not the single unnamed vector every existing
    /// collection was created with), not something that can silently
    /// upgrade an install in place. Turning it on the first time recreates
    /// the <c>documents</c> collection and empties it — see
    /// <c>QdrantVectorStore</c>'s class doc comment — so re-run indexing
    /// afterward. Also needs Qdrant 1.15.2+ (server-side BM25); an older
    /// instance fails clearly at startup rather than mid-query.
    ///
    /// Why bother: pure vector similarity can miss an exact term a
    /// question hinges on (a product code, a person's name, an acronym)
    /// when that term doesn't shift the embedding much relative to the
    /// surrounding text — keyword matching catches exactly that case,
    /// vector similarity catches paraphrases/synonyms keyword matching
    /// would miss. Fusing both covers more than either alone.
    ///
    /// Real trade-off, not a strict improvement: fused (RRF) scores are
    /// rank-based (small fractions, roughly <c>1/(60+rank)</c>), not
    /// cosine similarity — <see cref="MinRelevanceScore"/> and
    /// <see cref="EnableAdaptiveThreshold"/>'s math are both calibrated
    /// against cosine scores and DON'T apply to fused results. When this
    /// is on, <c>RagRetrieval</c>/<c>AnswerUseCase</c> skip both and use
    /// <see cref="HybridRelevanceRatio"/> instead — see its doc comment —
    /// which prunes candidates the same "relative to this query's best
    /// match" way, just rescaled for RRF's math.
    /// </summary>
    public bool EnableHybridSearch { get; set; } = false;

    /// <summary>
    /// Hybrid-search equivalent of <see cref="AdaptiveThresholdMargin"/> —
    /// only used when <see cref="EnableHybridSearch"/> is on. A fused (RRF)
    /// score isn't cosine similarity (see <see cref="EnableHybridSearch"/>'s
    /// doc comment), so an absolute margin like
    /// <see cref="AdaptiveThresholdMargin"/> doesn't transfer: RRF scores
    /// are tiny fractions whose exact scale depends on Qdrant's internal
    /// <c>k</c> constant and how many prefetch legs a candidate matched.
    /// A ratio does transfer, because it's scale-free — this keeps only
    /// candidates whose fused score is at least this fraction of THIS
    /// query's own top-scoring candidate, dropping the padding that
    /// otherwise fills out <see cref="TopK"/> with whatever ranked next
    /// however weakly (the real bug this closes: unrelated audio/image/PDF
    /// chunks showing up as citations alongside the two genuinely relevant
    /// ones for an unrelated query). 0.5 is a reasoned starting point, not
    /// measured-optimal — RRF's curve is intentionally flat for lower
    /// ranks, so this is a coarser cut than cosine-based adaptive
    /// thresholding, not an equally precise one; tighten (raise) it if
    /// weak matches still get through, loosen (lower) it if genuinely
    /// relevant runners-up are getting cut.
    /// </summary>
    public float HybridRelevanceRatio { get; set; } = 0.5f;
}
