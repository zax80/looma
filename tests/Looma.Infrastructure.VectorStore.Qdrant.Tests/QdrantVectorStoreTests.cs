using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Looma.Core.Exceptions;
using Looma.Infrastructure.VectorStore.Qdrant;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

public sealed class QdrantVectorStoreTests : IClassFixture<QdrantFixture>
{
    private const int Dimensions = 4;

    private readonly QdrantFixture _fixture;
    private readonly ITestOutputHelper _output;

    public QdrantVectorStoreTests(QdrantFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private bool SkipIfUnavailable()
    {
        if (_fixture.IsAvailable)
        {
            return false;
        }

        _output.WriteLine(
            "SKIPPED: no Qdrant reachable at the configured test endpoint. " +
            "Run `docker run -p 6333:6333 qdrant/qdrant` (or set LOOMA_TEST_QDRANT_ENDPOINT) " +
            "to exercise this suite for real, per CLAUDE.md's no-mocks-only rule for Infrastructure.*.");
        return true;
    }

    [Fact]
    public async Task Upsert_ThenSearch_ReturnsTheUpsertedPointAboveTheQueryVector()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var store = _fixture.CreateStore();
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);

        var metadata = new ChunkMetadata
        {
            SourcePath = "test.txt",
            MediaType = MediaType.Text,
            ChunkIndex = 0,
            IndexedAtUtc = DateTimeOffset.UtcNow
        };

        var record = new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]),
            Metadata = metadata,
            Content = "hello world"
        };

        await store.UpsertAsync(VectorCollection.Documents, [record]);

        var results = new List<VectorSearchResult>();
        await foreach (var result in store.SearchAsync(VectorCollection.Documents, new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), topK: 5))
        {
            results.Add(result);
        }

        Assert.Contains(results, r => r.Id == record.Id && r.Content == "hello world");
    }

    [Fact]
    public async Task Count_ReflectsNumberOfUpsertedPoints()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var store = _fixture.CreateStore();
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);

        var before = await store.CountAsync(VectorCollection.Documents);

        var records = Enumerable.Range(0, 3).Select(i => new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([i, 0f, 0f, 0f]),
            Metadata = new ChunkMetadata
            {
                SourcePath = $"test-{i}.txt",
                MediaType = MediaType.Text,
                ChunkIndex = i,
                IndexedAtUtc = DateTimeOffset.UtcNow
            }
        }).ToList();

        await store.UpsertAsync(VectorCollection.Documents, records);

        var after = await store.CountAsync(VectorCollection.Documents);

        Assert.Equal(before + records.Count, after);
    }

    [Fact]
    public async Task Delete_RemovesThePointFromSubsequentSearches()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var store = _fixture.CreateStore();
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);

        var record = new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([0f, 1f, 0f, 0f]),
            Metadata = new ChunkMetadata
            {
                SourcePath = "to-delete.txt",
                MediaType = MediaType.Text,
                ChunkIndex = 0,
                IndexedAtUtc = DateTimeOffset.UtcNow
            }
        };

        await store.UpsertAsync(VectorCollection.Documents, [record]);
        await store.DeleteAsync(VectorCollection.Documents, [record.Id]);

        var results = new List<VectorSearchResult>();
        await foreach (var result in store.SearchAsync(VectorCollection.Documents, new ReadOnlyMemory<float>([0f, 1f, 0f, 0f]), topK: 10))
        {
            results.Add(result);
        }

        Assert.DoesNotContain(results, r => r.Id == record.Id);
    }

    [Fact]
    public async Task ClearCollection_RemovesAllPoints_AndCollectionStillWorksAfterwards()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var store = _fixture.CreateStore();
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);

        await store.UpsertAsync(VectorCollection.Documents, [new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([1f, 1f, 0f, 0f]),
            Metadata = new ChunkMetadata
            {
                SourcePath = "to-clear.txt",
                MediaType = MediaType.Text,
                ChunkIndex = 0,
                IndexedAtUtc = DateTimeOffset.UtcNow
            }
        }]);

        await store.ClearCollectionAsync(VectorCollection.Documents);

        // The collection itself is gone at this point — EnsureCollectionAsync
        // must be able to recreate it lazily, same as on a totally fresh index run.
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);
        var count = await store.CountAsync(VectorCollection.Documents);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ClearCollection_OnAlreadyMissingCollection_DoesNotThrow()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        // A dedicated, guaranteed-never-created collection name — the
        // fixture's own Documents collection may already exist by the time
        // this test runs (other tests in this class share it), which would
        // defeat the point of testing the "doesn't exist yet" path.
        var neverCreatedName = $"looma_test_never_created_{Guid.NewGuid():N}";
        var store = new QdrantVectorStore(
            _fixture.HttpClient,
            Options.Create(new QdrantOptions
            {
                Endpoint = _fixture.HttpClient.BaseAddress!.ToString(),
                Collections = new QdrantCollectionNames { Documents = neverCreatedName, Images = neverCreatedName }
            }));

        var exception = await Record.ExceptionAsync(() => store.ClearCollectionAsync(VectorCollection.Documents));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DocumentsAndImagesCollections_AreIsolatedFromEachOther()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var store = _fixture.CreateStore();
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);
        await store.EnsureCollectionAsync(VectorCollection.Images, Dimensions);

        // Other tests in this class share the fixture's Documents collection,
        // so assert on the delta caused by this test's own write, not an
        // absolute count — the isolation property under test is "writing to
        // Images doesn't touch Documents", not "Documents starts empty".
        var documentsCountBefore = await store.CountAsync(VectorCollection.Documents);
        var imagesCountBefore = await store.CountAsync(VectorCollection.Images);

        var imageRecord = new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([0f, 0f, 1f, 0f]),
            Metadata = new ChunkMetadata
            {
                SourcePath = "photo.jpg",
                MediaType = MediaType.Image,
                ChunkIndex = 0,
                IndexedAtUtc = DateTimeOffset.UtcNow
            }
        };

        await store.UpsertAsync(VectorCollection.Images, [imageRecord]);

        var documentsCountAfter = await store.CountAsync(VectorCollection.Documents);
        var imagesCountAfter = await store.CountAsync(VectorCollection.Images);

        Assert.Equal(documentsCountBefore, documentsCountAfter);
        Assert.Equal(imagesCountBefore + 1, imagesCountAfter);
    }

    // ---- Qdrant unreachable (not gated behind SkipIfUnavailable — these
    // deliberately point at a real, guaranteed-nothing-listening endpoint,
    // independent of whether the fixture's actual Qdrant instance is up) ----

    /// <summary>
    /// A real, live socket connection attempt against a port nothing is
    /// listening on — per CLAUDE.md's no-mocks-only rule for
    /// Infrastructure.*, this is a genuine network failure (a real
    /// SocketException/HttpRequestException), not a faked-up
    /// HttpMessageHandler standing in for one. Reproduces exactly what a
    /// stopped local Qdrant container looks like from this class's
    /// perspective — see VectorStoreUnavailableException's doc comment for
    /// the real end-to-end bug this was found fixing.
    /// </summary>
    private static QdrantVectorStore CreateUnreachableStore() => new(
        new HttpClient { BaseAddress = new Uri("http://localhost:1"), Timeout = TimeSpan.FromSeconds(3) },
        Options.Create(new QdrantOptions
        {
            Endpoint = "http://localhost:1",
            Collections = new QdrantCollectionNames { Documents = "unreachable_test", Images = "unreachable_test" }
        }));

    [Fact]
    public async Task SearchAsync_QdrantUnreachable_ThrowsVectorStoreUnavailableException()
    {
        var store = CreateUnreachableStore();

        var ex = await Assert.ThrowsAsync<VectorStoreUnavailableException>(async () =>
        {
            await foreach (var _ in store.SearchAsync(VectorCollection.Documents, new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]), topK: 5))
            {
            }
        });

        Assert.Contains("Qdrant", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task CountAsync_QdrantUnreachable_ThrowsVectorStoreUnavailableException()
    {
        var store = CreateUnreachableStore();

        var ex = await Assert.ThrowsAsync<VectorStoreUnavailableException>(
            () => store.CountAsync(VectorCollection.Documents));

        Assert.Contains("Qdrant", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task EnsureCollectionAsync_QdrantUnreachable_ThrowsVectorStoreUnavailableException()
    {
        var store = CreateUnreachableStore();

        await Assert.ThrowsAsync<VectorStoreUnavailableException>(
            () => store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions));
    }

    // ---- Hybrid search — real Qdrant only, per CLAUDE.md's no-mocks-only
    // rule. Server-side BM25 needs Qdrant 1.15.2+ (see QdrantVectorStore's
    // class doc comment); a too-old test instance is reported and skipped
    // rather than failed, since that's an environment fact, not a bug. ----

    /// <summary>
    /// Returns true (and reports why) if this run's Qdrant is reachable but
    /// too old for hybrid search — <see cref="SkipIfUnavailable"/> already
    /// covers "not reachable at all"; this covers the other real reason a
    /// hybrid test can't run here.
    /// </summary>
    private async Task<bool> SkipIfHybridSearchUnsupportedAsync(QdrantVectorStore store, string hybridCollectionName)
    {
        try
        {
            await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions, enableHybridSearch: true);
            return false;
        }
        catch (QdrantRequestException ex) when (ex.Message.Contains("1.15.2", StringComparison.Ordinal))
        {
            _output.WriteLine($"SKIPPED: {ex.Message}");
            return true;
        }
    }

    [Fact]
    public async Task HybridSearch_KeywordMatchOutranksUnrelatedButCloserVector_WhenFusedWithBm25()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        // A dedicated collection, NOT the fixture's shared Documents
        // collection — every other test in this class expects Documents to
        // keep its plain (unnamed-vector) schema, and EnsureCollectionAsync
        // only ever migrates non-hybrid → hybrid, never back. Recreating the
        // SHARED collection as hybrid here would silently break whichever
        // plain-schema test happens to run after this one (xUnit doesn't
        // guarantee test order within a class) — a real failure mode hit
        // live: "Wrong input: Not existing vector name error" on a plain
        // UpsertAsync once the shared collection had already been migrated.
        var name = $"looma_test_hybrid_{Guid.NewGuid():N}";
        var store = new QdrantVectorStore(
            _fixture.HttpClient,
            Options.Create(new QdrantOptions
            {
                Endpoint = _fixture.HttpClient.BaseAddress!.ToString(),
                Collections = new QdrantCollectionNames { Documents = name, Images = name }
            }));

        if (await SkipIfHybridSearchUnsupportedAsync(store, name))
        {
            return;
        }

        try
        {
            // Same embedding for both — an all-vector search genuinely can't
            // tell these apart, so ranking the BM25-matching one first is
            // only possible because the sparse/keyword leg contributed to
            // the fused result. That's the actual behavior under test, not
            // merely "hybrid search runs without throwing."
            var sharedEmbedding = new ReadOnlyMemory<float>([0.5f, 0.5f, 0.5f, 0.5f]);

            var matching = new VectorRecord
            {
                Id = Guid.NewGuid().ToString(),
                Embedding = sharedEmbedding,
                Content = "The Bonibon caramel wafer recipe uses hazelnut cream.",
                Metadata = new ChunkMetadata { SourcePath = "bonibon.txt", MediaType = MediaType.Text, ChunkIndex = 0, IndexedAtUtc = DateTimeOffset.UtcNow }
            };
            var unrelated = new VectorRecord
            {
                Id = Guid.NewGuid().ToString(),
                Embedding = sharedEmbedding,
                Content = "Quantum computers use superconducting qubits at millikelvin temperatures.",
                Metadata = new ChunkMetadata { SourcePath = "quantum.txt", MediaType = MediaType.Text, ChunkIndex = 0, IndexedAtUtc = DateTimeOffset.UtcNow }
            };

            await store.UpsertAsync(VectorCollection.Documents, [matching, unrelated], enableHybridSearch: true);

            var results = new List<VectorSearchResult>();
            await foreach (var result in store.SearchAsync(
                VectorCollection.Documents, sharedEmbedding, topK: 5, queryText: "Bonibon caramel recipe"))
            {
                results.Add(result);
            }

            Assert.NotEmpty(results);
            Assert.Equal(matching.Id, results[0].Id);
        }
        finally
        {
            await _fixture.HttpClient.DeleteAsync($"/collections/{name}");
        }
    }

    [Fact]
    public async Task EnsureCollectionAsync_ExistingNonHybridCollection_MigratesWhenHybridRequested()
    {
        if (SkipIfUnavailable())
        {
            return;
        }

        var name = $"looma_test_migrate_{Guid.NewGuid():N}";
        var store = new QdrantVectorStore(
            _fixture.HttpClient,
            Options.Create(new QdrantOptions
            {
                Endpoint = _fixture.HttpClient.BaseAddress!.ToString(),
                Collections = new QdrantCollectionNames { Documents = name, Images = name }
            }));

        // Old (pre-hybrid) schema first — a point in it so migration
        // (recreate) is actually observable via the count resetting.
        await store.EnsureCollectionAsync(VectorCollection.Documents, Dimensions);
        await store.UpsertAsync(VectorCollection.Documents, [new VectorRecord
        {
            Id = Guid.NewGuid().ToString(),
            Embedding = new ReadOnlyMemory<float>([1f, 0f, 0f, 0f]),
            Content = "pre-migration point",
            Metadata = new ChunkMetadata { SourcePath = "old.txt", MediaType = MediaType.Text, ChunkIndex = 0, IndexedAtUtc = DateTimeOffset.UtcNow }
        }]);

        if (await SkipIfHybridSearchUnsupportedAsync(store, name))
        {
            await _fixture.HttpClient.DeleteAsync($"/collections/{name}");
            return;
        }

        var countAfterMigration = await store.CountAsync(VectorCollection.Documents);

        // Migration means recreation — the old schema's data doesn't
        // survive (see QdrantVectorStore's class doc comment for why this
        // is the deliberate, explicitly-chosen behavior, not a bug).
        Assert.Equal(0, countAfterMigration);

        await _fixture.HttpClient.DeleteAsync($"/collections/{name}");
    }
}
