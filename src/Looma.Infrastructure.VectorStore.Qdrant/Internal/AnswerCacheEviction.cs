namespace Looma.Infrastructure.VectorStore.Qdrant.Internal;

/// <summary>
/// Pure trimming logic for the exact-match cache's in-memory dictionary —
/// no I/O, so directly unit-testable. NOT true LRU, deliberately: real LRU
/// needs a "last accessed" timestamp updated on every READ, which would add
/// a write-back cost to lookups — the exact thing
/// <see cref="AnswerCacheMemoryStore"/> exists to avoid. Evicting the
/// OLDEST entries by <see cref="ExactCacheEntry.CreatedAtUtc"/> instead is
/// a simpler bound that still stops unconstrained growth for a long-running
/// install, at the cost of being "oldest first" rather than "least
/// recently used first" — a coarser policy, not a precise one.
/// </summary>
internal static class AnswerCacheEviction
{
    /// <summary>
    /// Mutates <paramref name="entries"/> in place, removing the oldest
    /// entries until its count is at or under <paramref name="maxEntries"/>.
    /// A <paramref name="maxEntries"/> of zero or less disables eviction
    /// entirely (unbounded growth) — the caller's responsibility to check,
    /// not this method's; kept here anyway as a safe no-op rather than
    /// evicting everything.
    /// </summary>
    public static void EvictOldestIfOverCapacity(Dictionary<string, ExactCacheEntry> entries, int maxEntries)
    {
        if (maxEntries <= 0 || entries.Count <= maxEntries)
        {
            return;
        }

        var toRemove = entries
            .OrderBy(kv => kv.Value.CreatedAtUtc)
            .Take(entries.Count - maxEntries)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            entries.Remove(key);
        }
    }
}
