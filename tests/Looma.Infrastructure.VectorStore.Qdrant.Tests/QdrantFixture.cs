using Looma.Infrastructure.VectorStore.Qdrant;
using Microsoft.Extensions.Options;
using Xunit;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

/// <summary>
/// Per CLAUDE.md: Infrastructure.* tests run against a real local Qdrant
/// instance, not mocks-only. This fixture pings that instance once; if it
/// isn't reachable (e.g. this repo cloned somewhere without Qdrant running
/// yet — `docker run -p 6333:6333 qdrant/qdrant`), tests report themselves
/// as skipped rather than failing the whole suite or, worse, silently
/// passing against a mock.
///
/// Uses uniquely-named collections per run and tears them down afterwards
/// so repeated runs never collide or leave state behind.
/// </summary>
public sealed class QdrantFixture : IAsyncLifetime
{
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    public HttpClient HttpClient { get; }
    public bool IsAvailable { get; private set; }
    public string DocumentsCollection { get; }
    public string ImagesCollection { get; }

    public QdrantFixture()
    {
        var endpoint = Environment.GetEnvironmentVariable("LOOMA_TEST_QDRANT_ENDPOINT") ?? "http://localhost:6333";
        HttpClient = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(5) };
        DocumentsCollection = $"looma_test_documents_{_runId}";
        ImagesCollection = $"looma_test_images_{_runId}";
    }

    public QdrantVectorStore CreateStore() => new(
        HttpClient,
        Options.Create(new QdrantOptions
        {
            Endpoint = HttpClient.BaseAddress!.ToString(),
            Collections = new QdrantCollectionNames
            {
                Documents = DocumentsCollection,
                Images = ImagesCollection
            }
        }));

    public async Task InitializeAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync("/collections");
            IsAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (IsAvailable)
        {
            try
            {
                await HttpClient.DeleteAsync($"/collections/{DocumentsCollection}");
                await HttpClient.DeleteAsync($"/collections/{ImagesCollection}");
            }
            catch
            {
                // Best-effort cleanup; don't fail the run over teardown.
            }
        }

        HttpClient.Dispose();
    }
}
