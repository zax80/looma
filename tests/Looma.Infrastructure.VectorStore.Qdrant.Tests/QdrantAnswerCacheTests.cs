using Looma.Core.Exceptions;
using Looma.Infrastructure.VectorStore.Qdrant;
using Microsoft.Extensions.Options;
using Xunit;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

/// <summary>
/// Covers <see cref="QdrantAnswerCache.ClearAsync"/>'s connectivity-failure
/// handling specifically — the one operation on this class that
/// deliberately doesn't swallow failures (see the class doc comment).
/// Real, uncovered lookup/store-path testing already exists indirectly via
/// <c>AnswerUseCase</c>'s own behavior; this file exists only for the
/// clear-cache-while-Qdrant-is-down case added alongside
/// <see cref="VectorStoreUnavailableException"/>.
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
            }));

        var ex = await Assert.ThrowsAsync<VectorStoreUnavailableException>(() => cache.ClearAsync());

        Assert.Contains("Qdrant", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    public void Dispose()
    {
        if (File.Exists(_tempCacheFilePath))
        {
            File.Delete(_tempCacheFilePath);
        }
    }
}
