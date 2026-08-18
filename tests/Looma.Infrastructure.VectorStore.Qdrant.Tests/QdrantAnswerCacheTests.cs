using Looma.Core.Entities;
using Looma.Core.Exceptions;
using Looma.Infrastructure.VectorStore.Qdrant;
using Looma.Infrastructure.VectorStore.Qdrant.Internal;
using Microsoft.Extensions.Options;
using Xunit;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

/// <summary>
/// Covers <see cref="QdrantAnswerCache.ClearAsync"/>'s connectivity-failure
/// handling, plus the exact-match layer's in-memory caching and eviction
/// wiring — these need real file I/O but never touch Qdrant, so unlike the
/// <c>ClearAsync</c> test below they don't need a reachable (or fake)
/// Qdrant endpoint. Real, uncovered lookup/store-path testing otherwise
/// already exists indirectly via <c>AnswerUseCase</c>'s own behavior.
/// </summary>
public sealed class QdrantAnswerCacheTests : IDisposable
{
    private readonly string _tempCacheFilePath = Path.Combine(Path.GetTempPath(), $"looma-test-cache-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task ClearAsync_QdrantUnreachable_ThrowsVectorStoreUnavailableException()
    {
        // Same "real socket, not a mock" reasoning as
        // QdrantVectorStoreTests's unreachable-store tests.
        var cache = new QdrantAnswerCache(
            new HttpClient { BaseAddress = new Uri("http://localhost:1"), Timeout = TimeSpan.FromSeconds(3) },
            Options.Create(new AnswerCacheOptions
            {
                Enabled = true,
                FilePath = _tempCacheFilePath,
                CollectionName = "unreachable_test_cache"
            }),
            new AnswerCacheMemoryStore());

        var ex = await Assert.ThrowsAsync<VectorStoreUnavailableException>(() => cache.ClearAsync());

        Assert.Contains("Qdrant", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task TryGetExactAsync_FileDeletedExternallyAfterStore_StillServesFromMemory()
    {
        // Proves the in-memory-first read path (LoadFileNoLockAsync) is
        // actually wired up: once StoreAsync has populated the shared
        // AnswerCacheMemoryStore, a lookup must succeed even if the file
        // backing it is gone — the cache is deliberately this process's
        // sole writer, so it should never need to re-read disk after the
        // first load.
        var cache = new QdrantAnswerCache(
            new HttpClient { BaseAddress = new Uri("http://localhost:1") },
            Options.Create(new AnswerCacheOptions
            {
                Enabled = true,
                FilePath = _tempCacheFilePath,
                CollectionName = "unused_test_cache"
            }),
            new AnswerCacheMemoryStore());

        await cache.StoreAsync(
            "What is Looma?",
            ReadOnlyMemory<float>.Empty,
            "Looma is a local-first document intelligence platform.",
            citations: [],
            documentsVersion: 1);

        Assert.True(File.Exists(_tempCacheFilePath));
        File.Delete(_tempCacheFilePath);

        var hit = await cache.TryGetExactAsync("What is Looma?", documentsVersion: 1);

        Assert.NotNull(hit);
        Assert.Equal("Looma is a local-first document intelligence platform.", hit!.AnswerText);
    }

    [Fact]
    public async Task StoreAsync_ExceedsMaxExactEntries_EvictsOldestEntries()
    {
        var cache = new QdrantAnswerCache(
            new HttpClient { BaseAddress = new Uri("http://localhost:1") },
            Options.Create(new AnswerCacheOptions
            {
                Enabled = true,
                FilePath = _tempCacheFilePath,
                CollectionName = "unused_test_cache",
                MaxExactEntries = 2
            }),
            new AnswerCacheMemoryStore());

        await cache.StoreAsync("first question", ReadOnlyMemory<float>.Empty, "first answer", [], documentsVersion: 1);
        await cache.StoreAsync("second question", ReadOnlyMemory<float>.Empty, "second answer", [], documentsVersion: 1);
        await cache.StoreAsync("third question", ReadOnlyMemory<float>.Empty, "third answer", [], documentsVersion: 1);

        Assert.Null(await cache.TryGetExactAsync("first question", documentsVersion: 1));
        Assert.NotNull(await cache.TryGetExactAsync("second question", documentsVersion: 1));
        Assert.NotNull(await cache.TryGetExactAsync("third question", documentsVersion: 1));
    }

    public void Dispose()
    {
        if (File.Exists(_tempCacheFilePath))
        {
            File.Delete(_tempCacheFilePath);
        }
    }
}
