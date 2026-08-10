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
}
