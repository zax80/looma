using Looma.Core.Abstractions;
using Looma.Core.Entities;
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
}
