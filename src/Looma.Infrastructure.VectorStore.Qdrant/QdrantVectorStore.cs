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
///
/// Hybrid search (see <c>RagOptions.EnableHybridSearch</c>): when a caller
/// opts in, a collection is created with a NAMED dense vector ("dense")
/// plus a NAMED sparse vector ("sparse", BM25-style, computed server-side
/// by Qdrant itself from each record's <see cref="VectorRecord.Content"/>
/// — no client-side tokenizer or ML model needed, confirmed against
/// Qdrant's own docs: this has been a genuine self-hosted, non-Cloud-only
/// feature since Qdrant 1.15.2, not a hosted-only convenience) instead of
/// a single unnamed dense vector. Queries then fetch both a dense-nearest
/// list and a BM25-keyword list via Qdrant's Query API and fuse them with
/// Reciprocal Rank Fusion.
///
/// This is a real, breaking schema difference — Qdrant has no in-place way
/// to add a sparse vector to an already-created collection — not an
/// additive option. <see cref="EnsureCollectionAsync"/> detects an
/// existing collection still on the old (unnamed-vector) schema when
/// hybrid search is requested and recreates it, per the explicit choice
/// made when this was built: simplicity over data preservation. Recreating
/// empties the collection — the caller is responsible for re-running
/// indexing afterward (same as <c>looma index --clear</c> today).
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

    private const string DenseVectorName = "dense";
    private const string SparseVectorName = "sparse";
    private const string Bm25Model = "qdrant/bm25";

    /// <summary>
    /// Server-side BM25 sparse vectors (see the class doc comment) are a
    /// Qdrant 1.15.2+ feature. Checked once per <see cref="EnsureCollectionAsync"/>
    /// call that actually requests hybrid search — not on every request —
    /// so a too-old Qdrant fails clearly and immediately at startup/indexing
    /// time instead of surfacing as a confusing mid-query 400 later.
    /// </summary>
    private static readonly Version MinimumHybridSearchVersion = new(1, 15, 2);

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
        bool enableHybridSearch = false,
        CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        if (enableHybridSearch)
        {
            await EnsureMinimumVersionForHybridSearchAsync(cancellationToken).ConfigureAwait(false);
        }

        using var existing = await QdrantConnectivity.SendAsync(
            () => _httpClient.GetAsync($"/collections/{name}", cancellationToken),
            $"check collection '{name}'", cancellationToken).ConfigureAwait(false);

        if (existing.StatusCode == HttpStatusCode.OK)
        {
            if (enableHybridSearch && !await HasSparseVectorConfiguredAsync(existing, cancellationToken).ConfigureAwait(false))
            {
                // Old (pre-hybrid) schema — see the class doc comment for
                // why this recreates rather than migrates in place.
                await DeleteCollectionAsync(name, cancellationToken).ConfigureAwait(false);
                await CreateHybridCollectionAsync(name, dimensions, cancellationToken).ConfigureAwait(false);
            }

            // Otherwise already exists with the right schema. Not
            // attempting a dimension-mismatch check here — Qdrant will
            // reject upserts with the wrong vector size on its own, which
            // surfaces the problem loudly rather than silently.
            return;
        }

        if (existing.StatusCode != HttpStatusCode.NotFound)
        {
            await ThrowIfUnsuccessful(existing, $"check collection '{name}'", cancellationToken).ConfigureAwait(false);
        }

        if (enableHybridSearch)
        {
            await CreateHybridCollectionAsync(name, dimensions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await CreateCollectionAsync(name, dimensions, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateCollectionAsync(string name, int dimensions, CancellationToken cancellationToken)
    {
        var request = new CreateCollectionRequest
        {
            Vectors = new VectorParams { Size = dimensions, Distance = DistanceMetric }
        };

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PutAsJsonAsync($"/collections/{name}", request, cancellationToken),
            $"create collection '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"create collection '{name}'", cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateHybridCollectionAsync(string name, int dimensions, CancellationToken cancellationToken)
    {
        var request = new CreateHybridCollectionRequest
        {
            Vectors = new Dictionary<string, VectorParams>
            {
                [DenseVectorName] = new() { Size = dimensions, Distance = DistanceMetric }
            },
            SparseVectors = new Dictionary<string, SparseVectorParams>
            {
                [SparseVectorName] = new()
            }
        };

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PutAsJsonAsync($"/collections/{name}", request, cancellationToken),
            $"create hybrid collection '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"create hybrid collection '{name}'", cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteCollectionAsync(string name, CancellationToken cancellationToken)
    {
        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.DeleteAsync($"/collections/{name}", cancellationToken),
            $"recreate collection '{name}' for hybrid search", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"recreate collection '{name}' for hybrid search", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> HasSparseVectorConfiguredAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<CollectionInfoResponse>(cancellationToken).ConfigureAwait(false);
        return body?.Result?.Config?.Params?.SparseVectors is { Count: > 0 };
    }

    private async Task EnsureMinimumVersionForHybridSearchAsync(CancellationToken cancellationToken)
    {
        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.GetAsync("/", cancellationToken),
            "check Qdrant version for hybrid search", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, "check Qdrant version for hybrid search", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<QdrantRootResponse>(cancellationToken).ConfigureAwait(false);

        // A version string Qdrant itself doesn't report in a parseable
        // shape is treated as "can't confirm, don't block" rather than a
        // hard failure — this check exists to turn a confusing mid-query
        // 400 into a clear message, not to add a new way for a working
        // setup to fail outright over a version-string parsing quirk.
        if (body?.Version is not { } versionText || !Version.TryParse(versionText, out var version))
        {
            return;
        }

        if (version < MinimumHybridSearchVersion)
        {
            throw new QdrantRequestException(
                $"RagOptions.EnableHybridSearch is on, but this Qdrant instance reports version " +
                $"{versionText} — server-side BM25 hybrid search needs {MinimumHybridSearchVersion} or newer. " +
                "Upgrade Qdrant, or set EnableHybridSearch back to false.");
        }
    }

    public async Task UpsertAsync(
        VectorCollection collection,
        IReadOnlyList<VectorRecord> records,
        bool enableHybridSearch = false,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        var name = ResolveCollectionName(collection);

        if (enableHybridSearch)
        {
            var hybridPoints = records
                .Select(r => new HybridPointStruct
                {
                    Id = r.Id,
                    Vector = new HybridVectors
                    {
                        Dense = r.Embedding.ToArray(),
                        // Empty text still produces a valid (all-zero)
                        // sparse vector rather than failing the upsert —
                        // a chunk with no extractable content shouldn't
                        // block the rest of a batch.
                        Sparse = new Bm25Document { Text = r.Content ?? string.Empty, Model = Bm25Model }
                    },
                    Payload = QdrantPayloadMapper.ToPayload(r.Metadata, r.Content)
                })
                .ToList();

            var hybridRequest = new UpsertHybridPointsRequest { Points = hybridPoints };

            using var hybridResponse = await QdrantConnectivity.SendAsync(
                () => _httpClient.PutAsJsonAsync($"/collections/{name}/points?wait=true", hybridRequest, cancellationToken),
                $"upsert {hybridPoints.Count} hybrid point(s) into '{name}'", cancellationToken).ConfigureAwait(false);
            await ThrowIfUnsuccessful(hybridResponse, $"upsert {hybridPoints.Count} hybrid point(s) into '{name}'", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var points = records
            .Select(r => new PointStruct
            {
                Id = r.Id,
                Vector = r.Embedding.ToArray(),
                Payload = QdrantPayloadMapper.ToPayload(r.Metadata, r.Content)
            })
            .ToList();

        var request = new UpsertPointsRequest { Points = points };

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PutAsJsonAsync($"/collections/{name}/points?wait=true", request, cancellationToken),
            $"upsert {points.Count} point(s) into '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"upsert {points.Count} point(s) into '{name}'", cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<VectorSearchResult> SearchAsync(
        VectorCollection collection,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float? minRelevanceScore = null,
        string? queryText = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);
        var useHybrid = !string.IsNullOrWhiteSpace(queryText);

        // The actual network call/parsing lives in a non-iterator helper —
        // `yield return` isn't allowed inside a try/catch with a catch
        // clause (only try/finally), and SendAsync's connectivity-failure
        // translation needs exactly that, so it can't live inline here.
        var results = useHybrid
            ? await ExecuteHybridSearchAsync(name, queryEmbedding, queryText!, topK, cancellationToken).ConfigureAwait(false)
            : await ExecuteSearchAsync(name, queryEmbedding, topK, minRelevanceScore, cancellationToken).ConfigureAwait(false);

        // Qdrant's REST search returns one JSON payload, not a chunked/SSE
        // stream — a single top-k query isn't the "long-running operation"
        // CLAUDE.md's streaming rule targets (that's indexing a folder /
        // generating an answer). Yielding here still keeps the abstraction
        // consistent with the rest of the pipeline and avoids callers ever
        // needing to materialize a List<T> themselves.
        foreach (var point in results)
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

    private async Task<IReadOnlyList<ScoredPoint>> ExecuteSearchAsync(
        string name,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float? minRelevanceScore,
        CancellationToken cancellationToken)
    {
        var request = new SearchPointsRequest
        {
            Vector = queryEmbedding.ToArray(),
            Limit = topK,
            ScoreThreshold = minRelevanceScore,
            WithPayload = true
        };

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PostAsJsonAsync($"/collections/{name}/points/search", request, cancellationToken),
            $"search '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"search '{name}'", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<SearchPointsResponse>(cancellationToken).ConfigureAwait(false);
        return body?.Result ?? [];
    }

    /// <summary>
    /// Fetches <c>topK * PrefetchOverfetchFactor</c> candidates from each of
    /// the dense and BM25 legs, then lets Qdrant's Query API fuse them with
    /// Reciprocal Rank Fusion down to <paramref name="topK"/> — the
    /// standard shape for hybrid search (see the class doc comment).
    /// Deliberately does NOT pass a score threshold: RRF's fused score is a
    /// rank-based value (small fractions, roughly <c>1/(60+rank)</c>), not
    /// a cosine similarity — applying <c>RagOptions.MinRelevanceScore</c>
    /// or the adaptive-threshold math to it would filter out nearly
    /// everything. See <c>RagOptions.EnableHybridSearch</c>'s doc comment.
    /// </summary>
    private async Task<IReadOnlyList<ScoredPoint>> ExecuteHybridSearchAsync(
        string name,
        ReadOnlyMemory<float> queryEmbedding,
        string queryText,
        int topK,
        CancellationToken cancellationToken)
    {
        const int PrefetchOverfetchFactor = 4;
        var prefetchLimit = topK * PrefetchOverfetchFactor;

        var request = new QueryPointsRequest
        {
            Prefetch =
            [
                new PrefetchQuery
                {
                    Query = new Bm25Document { Text = queryText, Model = Bm25Model },
                    Using = SparseVectorName,
                    Limit = prefetchLimit
                },
                new PrefetchQuery
                {
                    Query = queryEmbedding.ToArray(),
                    Using = DenseVectorName,
                    Limit = prefetchLimit
                }
            ],
            Query = new FusionQuery(),
            Limit = topK,
            WithPayload = true
        };

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PostAsJsonAsync($"/collections/{name}/points/query", request, cancellationToken),
            $"hybrid search '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"hybrid search '{name}'", cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadFromJsonAsync<QueryPointsResponse>(cancellationToken).ConfigureAwait(false);
        return body?.Result?.Points ?? [];
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

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PostAsJsonAsync($"/collections/{name}/points/delete?wait=true", request, cancellationToken),
            $"delete {ids.Count} point(s) from '{name}'", cancellationToken).ConfigureAwait(false);
        await ThrowIfUnsuccessful(response, $"delete {ids.Count} point(s) from '{name}'", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long> CountAsync(
        VectorCollection collection,
        CancellationToken cancellationToken = default)
    {
        var name = ResolveCollectionName(collection);

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.PostAsJsonAsync($"/collections/{name}/points/count", new CountPointsRequest { Exact = true }, cancellationToken),
            $"count '{name}'", cancellationToken).ConfigureAwait(false);
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

        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.DeleteAsync($"/collections/{name}", cancellationToken),
            $"clear collection '{name}'", cancellationToken).ConfigureAwait(false);

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
