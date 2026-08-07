using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>
/// Caches question/answer pairs so repeat (or near-repeat) questions can skip
/// the LLM generation entirely.
///
/// The exact-match and semantic-fallback lookups are deliberately two
/// separate methods rather than one combined call: exact-match only needs
/// the literal question string, while semantic-match needs its embedding —
/// which itself costs a real Ollama call. Splitting them lets the caller
/// (<c>AnswerUseCase</c>) check the cheap exact match first and skip
/// generating an embedding entirely on that hit, instead of paying for an
/// embedding call before every single cache lookup regardless of outcome.
///
/// Implementations must persist across process runs: the CLI is a fresh
/// process every invocation, so an in-memory cache would never see a hit.
///
/// A no-op implementation (always miss, store is a no-op) is a valid
/// implementation — callers always call these methods unconditionally;
/// whether caching is actually active is an infrastructure/config concern.
/// </summary>
public interface IAnswerCache
{
    /// <summary>
    /// Literal (normalized) question match — no embedding required. Only
    /// counts as a hit if the entry's <see cref="CachedAnswer.DocumentsVersion"/>
    /// equals <paramref name="documentsVersion"/>; otherwise the index has
    /// changed since the entry was written and it's treated as stale.
    /// </summary>
    Task<CachedAnswer?> TryGetExactAsync(
        string question,
        long documentsVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Similarity match against previously-asked questions, above a high
    /// threshold. Call only after <see cref="TryGetExactAsync"/> misses —
    /// this is the call that needs a real question embedding. Same
    /// <see cref="CachedAnswer.DocumentsVersion"/> staleness rule as the exact match.
    /// </summary>
    Task<CachedAnswer?> TryGetSemanticAsync(
        ReadOnlyMemory<float> questionEmbedding,
        long documentsVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Records a freshly generated answer so future matching questions can hit the cache.</summary>
    Task StoreAsync(
        string question,
        ReadOnlyMemory<float> questionEmbedding,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        long documentsVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wipes every cached entry (both the exact-match and semantic layers).
    /// The <see cref="CachedAnswer.DocumentsVersion"/> staleness check only
    /// catches a re-index — it has no way to know a prompt, model setting,
    /// or config value that affects generation changed, so a stale-but-
    /// still-"fresh"-by-that-check entry can otherwise mask a real fix
    /// indefinitely. Exists for exactly that recovery case — a real run hit
    /// this: a repeat question kept returning a wrong answer cached before
    /// a system-prompt fix, because nothing about the index had changed.
    /// Unlike the lookup/store methods, failures here should surface rather
    /// than fail silently — the caller explicitly asked for the cache to be
    /// gone, so pretending that succeeded when it didn't would be misleading.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
