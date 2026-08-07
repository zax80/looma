namespace Looma.Core.Entities;

/// <summary>
/// A previously generated answer, returned by <see cref="Abstractions.IAnswerCache"/>
/// on a cache hit (exact-match or semantic). Carries enough to satisfy the same
/// contract as a fresh <see cref="AnswerToken"/> stream would: the full answer text
/// plus the citations it was grounded in.
/// </summary>
public sealed record CachedAnswer
{
    public required string AnswerText { get; init; }

    public required IReadOnlyList<DocumentChunk> Citations { get; init; }

    /// <summary>
    /// The <c>documents</c> collection's chunk count at the moment this entry was
    /// written — the staleness signal. A cache lookup only counts as a hit when
    /// this matches the current chunk count; any re-index changes the count and
    /// silently invalidates every prior entry rather than risking a stale answer.
    /// </summary>
    public required long DocumentsVersion { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
