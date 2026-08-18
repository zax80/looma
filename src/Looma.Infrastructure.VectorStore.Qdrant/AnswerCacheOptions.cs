namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>
/// Binds to the <c>AnswerCache</c> section of config.json. Deliberately
/// separate from <see cref="QdrantOptions"/> — the answer cache reuses that
/// section's <c>Endpoint</c>/<c>ApiKey</c> (it's the same Qdrant instance)
/// but has its own settings that have nothing to do with the RAG vector
/// store proper.
/// </summary>
public sealed class AnswerCacheOptions
{
    public const string SectionName = "AnswerCache";

    public bool Enabled { get; set; } = true;

    /// <summary>Exact-match layer: a local JSON file keyed by normalized question text. Resolved relative to the working directory, same as RAG source paths.</summary>
    public string FilePath { get; set; } = "./.looma/answer-cache.json";

    /// <summary>
    /// Semantic-fallback layer: a Qdrant collection dedicated to cached
    /// question embeddings — deliberately not the <c>documents</c> or
    /// <c>images</c> collection (see architecture rule: never mix embedding
    /// spaces/purposes in one collection).
    /// </summary>
    public string CollectionName { get; set; } = "answer_cache";

    /// <summary>
    /// Cosine similarity a candidate question must clear to count as "the
    /// same question" for the semantic fallback. Deliberately strict: a
    /// false-positive hit here means confidently serving the wrong answer,
    /// which is worse than the latency this cache exists to avoid.
    /// </summary>
    public float SemanticSimilarityThreshold { get; set; } = 0.97f;

    /// <summary>
    /// Hours after which a cached answer that cites at least one web search
    /// result is treated as stale and no longer served — see
    /// <c>AnswerCacheStaleness</c>'s doc comment for why this exists
    /// separately from the <c>DocumentsVersion</c> staleness check (that
    /// one only catches LOCAL content changing via re-index; a web
    /// citation's real-world content can go stale on its own). Answers
    /// sourced entirely from local documents are unaffected. Null disables
    /// this check entirely — a web-sourced answer would then only ever be
    /// invalidated by a re-index, the previous (real gap) behavior. 24 is a
    /// reasoned starting point, not measured-optimal.
    /// </summary>
    public int? WebCitationMaxAgeHours { get; set; } = 24;

    /// <summary>
    /// Soft cap on the exact-match layer's entry count — once exceeded, the
    /// OLDEST entries (by creation time, not last-accessed — see
    /// <c>AnswerCacheEviction</c>'s doc comment for why not true LRU) are
    /// evicted on the next write until back under the cap. Zero or less
    /// disables eviction entirely (unbounded growth, the previous
    /// behavior). The semantic (Qdrant) layer is deliberately NOT capped by
    /// this — Qdrant scales with point count far better than repeatedly
    /// rewriting a flat JSON file does, so it's left unbounded for now;
    /// revisit if that becomes a real problem. 500 is a reasoned starting
    /// point, not measured-optimal.
    /// </summary>
    public int MaxExactEntries { get; set; } = 500;
}
