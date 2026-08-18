using Looma.Core.Entities;
using Looma.Infrastructure.VectorStore.Qdrant.Internal;
using Xunit;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

public sealed class AnswerCacheEvictionTests
{
    private static ExactCacheEntry MakeEntry(DateTimeOffset createdAtUtc) => new()
    {
        AnswerText = "answer",
        Citations = [],
        DocumentsVersion = 1,
        CreatedAtUtc = createdAtUtc
    };

    [Fact]
    public void EvictOldestIfOverCapacity_UnderCapacity_RemovesNothing()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new Dictionary<string, ExactCacheEntry>
        {
            ["a"] = MakeEntry(now),
            ["b"] = MakeEntry(now)
        };

        AnswerCacheEviction.EvictOldestIfOverCapacity(entries, maxEntries: 5);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void EvictOldestIfOverCapacity_OverCapacity_RemovesOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new Dictionary<string, ExactCacheEntry>
        {
            ["oldest"] = MakeEntry(now.AddHours(-3)),
            ["middle"] = MakeEntry(now.AddHours(-2)),
            ["newest"] = MakeEntry(now.AddHours(-1))
        };

        AnswerCacheEviction.EvictOldestIfOverCapacity(entries, maxEntries: 2);

        Assert.Equal(2, entries.Count);
        Assert.False(entries.ContainsKey("oldest"));
        Assert.True(entries.ContainsKey("middle"));
        Assert.True(entries.ContainsKey("newest"));
    }

    [Fact]
    public void EvictOldestIfOverCapacity_ZeroOrNegativeMaxEntries_DisablesEviction()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new Dictionary<string, ExactCacheEntry>
        {
            ["a"] = MakeEntry(now),
            ["b"] = MakeEntry(now)
        };

        AnswerCacheEviction.EvictOldestIfOverCapacity(entries, maxEntries: 0);
        AnswerCacheEviction.EvictOldestIfOverCapacity(entries, maxEntries: -1);

        Assert.Equal(2, entries.Count);
    }
}
