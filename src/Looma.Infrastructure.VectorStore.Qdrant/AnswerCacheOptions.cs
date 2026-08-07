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
}
