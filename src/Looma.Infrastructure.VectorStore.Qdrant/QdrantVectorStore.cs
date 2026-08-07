using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Looma.Infrastructure.VectorStore.Qdrant.Internal;
using Microsoft.Extensions.Options;

namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>
/// The one and only <see cref="IVectorStore"/> implementation (see
/// CLAUDE.md architecture rule 4 — do not add a second one). Talks to a
/// local or LAN Qdrant instance over its plain HTTP REST API.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore
{
    // Cosine is the right default for both embedding spaces this store
    // holds: nomic-embed-text (documents) and CLIP (images) are both
    // typically compared with cosine similarity, and it keeps scores in a
    // normalized range that lines up with config's MinRelevanceScore
    // threshold semantics. Not currently configurable — revisit if a
    // future embedding model needs a different metric.
    private const string DistanceMetric = "Cosine";

    private readonly HttpClient _httpClient;
    private readonly QdrantOptions _options;

    public QdrantVectorStore(HttpClient httpClient, IOptions<QdrantOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task EnsureCollectionAsync(
        VectorCollection collection,
        int dimensions,
        CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        using var existing = await _httpClient.GetAsync($"/collections/{name}", cancellationToken).ConfigureAwait(false);
        if (existing.StatusCode == HttpStatusCode.OK)
        {
            // Already exists. Not attempting a dimension-mismatch check here —
            // Qdrant will reject upserts with the wrong vector size on its own,
            // which surfaces the problem loudly rather than silently.
            return;
        }

        if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowIfUnsuccessful(existing, $"check collection '{name}'", cancellationToken).ConfigureAwait(false);
        }

        var request = new CreateCollectionRequest
        {
            Vectors = new VectorParams { Size = dimensions, Distance = DistanceMetric }
        };

        using var response = await _httpClient.PutAsJsonAsync($"/collections/{name}", request, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"create collection '{name}'", cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(
        VectorCollection collection,
        IReadOnlyList<VectorRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        var name = ResolveCollectionName(collection);

        var points = records
            .Select(r => new PointStruct
            {
                Id = r.Id,
                Vector = r.Embedding.ToArray(),
                Payload = QdrantPayloadMapper.ToPayload(r.Metadata, r.Content)
            })
            .ToList();

        var request = new UpsertPointsRequest { Points = points };

        using var response = await _httpClient
            .PutAsJsonAsync($"/collections/{name}/points?wait=true", request, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"upsert {points.Count} point(s) into '{name}'", cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<VectorSearchResult> SearchAsync(
        VectorCollection collection,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float? minRelevanceScore = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        var request = new SearchPointsRequest
        {
            Vector = queryEmbedding.ToArray(),
            Limit = topK,
            ScoreThreshold = minRelevanceScore,
            WithPayload = true
        };

        using var response = await _httpClient
            .PostAsJsonAsync($"/collections/{name}/points/search", request, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"search '{name}'", cancellationToken).ConfigureAwait(false);

        // Qdrant's REST search returns one JSON payload, not a chunked/SSE
        // stream — a single top-k query isn't the "long-running operation"
        // CLAUDE.md's streaming rule targets (that's indexing a folder /
        // generating an answer). Yielding here still keeps the abstraction
        // consistent with the rest of the pipeline and avoids callers ever
        // needing to materialize a List<T> themselves.
        var body = await response.Content.ReadFromJsonAsync<SearchPointsResponse>(cancellationToken)
            .ConfigureAwait(false);

        foreach (var point in body?.Result ?? [])
        {
            if (point.Payload is null)
            {
                continue;
            }

            yield return new VectorSearchResult
            {
                Id = point.Id,
                Score = point.Score,
                Content = point.Payload.Content,
                Metadata = QdrantPayloadMapper.ToMetadata(point.Payload)
            };
        }
    }

    public async Task DeleteAsync(
        VectorCollection collection,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var name = ResolveCollectionName(collection);
        var request = new DeletePointsRequest { Points = ids };

        using var response = await _httpClient
            .PostAsJsonAsync($"/collections/{name}/points/delete?wait=true", request, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"delete {ids.Count} point(s) from '{name}'", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> CountAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        using var response = await _httpClient
            .PostAsJsonAsync($"/collections/{name}/points/count", new CountPointsRequest { Exact = true }, cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"count '{name}'", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<CountPointsResponse>(cancellationToken)
            .ConfigureAwait(false);
        return body?.Result?.Count ?? 0;
    }

    public async Task ClearCollectionAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        using var response = await _httpClient.DeleteAsync($"/collections/{name}", cancellationToken).ConfigureAwait(false);

        // 404 means there was nothing to clear — that's the desired end
        // state, not a failure. EnsureCollectionAsync recreates it lazily
        // the next time something is indexed.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await ThrowIfUnsuccessful(response, $"clear collection '{name}'", cancellationToken).ConfigureAwait(false);
    }

    private string ResolveCollectionName(VectorCollection collection) => collection switch
    {
        VectorCollection.Documents => _options.Collections.Documents,
        VectorCollection.Images => _options.Collections.Images,
        _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, "Unknown vector collection.")
    };

    private static async Task ThrowIfUnsuccessful(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Deliberately not logging the request body here — it may contain
        // chunk content — only the status and Qdrant's own error text.
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new QdrantRequestException(
            $"Qdrant request to {action} failed with status {(int)response.StatusCode} ({response.StatusCode}): {errorBody}");
    }
}
