using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Looma.Infrastructure.VectorStore.Qdrant.Internal;
using Microsoft.Extensions.Options;

namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>
/// The one <see cref="IAnswerCache"/> implementation, backed by two stores:
///
/// 1. Exact-match — a local JSON file keyed by normalized question text.
///    Cheap, no network round trip, no embedding needed — the caller is
///    expected to try this first via <see cref="TryGetExactAsync"/>.
/// 2. Semantic fallback — a dedicated Qdrant collection (never the
///    <c>documents</c>/<c>images</c> collections) of previously-asked
///    question embeddings, via <see cref="TryGetSemanticAsync"/>.
///
/// Both layers key a hit on <c>DocumentsVersion</c> matching the current
/// chunk count — a re-index changes that count and silently invalidates
/// every prior entry rather than risking a stale answer.
///
/// Every operation here is best-effort: a cache read/write failure (file
/// permissions, Qdrant briefly unreachable, corrupt file) must never break
/// answering — it just falls through to a fresh generation.
/// </summary>
public sealed class QdrantAnswerCache : IAnswerCache
{
    private const string DistanceMetric = "Cosine";

    private readonly HttpClient _httpClient;
    private readonly AnswerCacheOptions _options;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public QdrantAnswerCache(HttpClient httpClient, IOptions<AnswerCacheOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<CachedAnswer?> TryGetExactAsync(
        string question,
        long documentsVersion,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        var entries = await LoadFileAsync(cancellationToken).ConfigureAwait(false);
        if (!entries.TryGetValue(AnswerCacheQuestionNormalizer.Normalize(question), out var entry))
        {
            return null;
        }

        if (entry.DocumentsVersion != documentsVersion)
        {
            return null;
        }

        return ToCachedAnswer(entry);
    }

    public async Task<CachedAnswer?> TryGetSemanticAsync(
        ReadOnlyMemory<float> questionEmbedding,
        long documentsVersion,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        return await TryGetSemanticCoreAsync(questionEmbedding, documentsVersion, cancellationToken).ConfigureAwait(false);
    }

    public async Task StoreAsync(
        string question,
        ReadOnlyMemory<float> questionEmbedding,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        long documentsVersion,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(answerText))
        {
            // An empty answer (e.g. the run was cancelled mid-stream) isn't
            // worth caching — it would just serve a blank "answer" later.
            return;
        }

        var createdAt = DateTimeOffset.UtcNow;

        await StoreExactAsync(question, answerText, citations, documentsVersion, createdAt, cancellationToken).ConfigureAwait(false);

        if (questionEmbedding.Length > 0)
        {
            await StoreSemanticAsync(question, questionEmbedding, answerText, citations, documentsVersion, createdAt, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Unlike the lookup/store paths, this doesn't swallow failures —
        // the caller explicitly asked for the cache to be gone.
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = ResolveFilePath();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _fileLock.Release();
        }

        // Uses the same shared connectivity-failure translation
        // QdrantVectorStore does — see QdrantConnectivity's doc comment for
        // why this used to be hand-rolled here instead, and the real gap
        // that caused (missing the TaskCanceledException/timeout case).
        // This method deliberately doesn't swallow failures otherwise (see
        // the class doc comment) — the caller explicitly asked for the
        // cache to be gone.
        using var response = await QdrantConnectivity.SendAsync(
            () => _httpClient.DeleteAsync($"/collections/{_options.CollectionName}", cancellationToken),
            $"clear the answer cache collection '{_options.CollectionName}'", cancellationToken).ConfigureAwait(false);

        // 404 means there was nothing to clear — that's the desired end
        // state, not a failure.
        if (response.StatusCode == HttpStatusCode.NotFound || response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new QdrantRequestException(
            $"Failed to clear answer cache collection '{_options.CollectionName}': " +
            $"{(int)response.StatusCode} ({response.StatusCode}) {errorBody}");
    }

    // ---- exact-match layer (local file) ----

    private async Task StoreExactAsync(
        string question,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        long documentsVersion,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadFileNoLockAsync(cancellationToken).ConfigureAwait(false);
            entries[AnswerCacheQuestionNormalizer.Normalize(question)] = new ExactCacheEntry
            {
                AnswerText = answerText,
                Citations = citations,
                DocumentsVersion = documentsVersion,
                CreatedAtUtc = createdAt
            };

            var path = ResolveFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, entries, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Best-effort: couldn't persist the cache entry, but the answer
            // itself was already generated successfully — don't fail the run over this.
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, ExactCacheEntry>> LoadFileAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadFileNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<Dictionary<string, ExactCacheEntry>> LoadFileNoLockAsync(CancellationToken cancellationToken)
    {
        var path = ResolveFilePath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, ExactCacheEntry>>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return loaded ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable cache file — treat as empty rather than crashing answer generation.
            return [];
        }
    }

    private string ResolveFilePath() => Path.GetFullPath(_options.FilePath);

    private static CachedAnswer ToCachedAnswer(ExactCacheEntry entry) => new()
    {
        AnswerText = entry.AnswerText,
        Citations = entry.Citations,
        DocumentsVersion = entry.DocumentsVersion,
        CreatedAtUtc = entry.CreatedAtUtc
    };

    // ---- semantic-fallback layer (Qdrant) ----

    private async Task<CachedAnswer?> TryGetSemanticCoreAsync(
        ReadOnlyMemory<float> questionEmbedding,
        long documentsVersion,
        CancellationToken cancellationToken)
    {
        if (questionEmbedding.Length == 0)
        {
            return null;
        }

        try
        {
            var request = new AnswerCacheSearchRequest
            {
                Vector = questionEmbedding.ToArray(),
                Limit = 1,
                ScoreThreshold = _options.SemanticSimilarityThreshold,
                WithPayload = true
            };

            using var response = await _httpClient
                .PostAsJsonAsync($"/collections/{_options.CollectionName}/points/search", request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Most commonly: the collection doesn't exist yet because
                // nothing has ever been cached. Either way, a miss.
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<AnswerCacheSearchResponse>(cancellationToken).ConfigureAwait(false);
            var best = body?.Result?.FirstOrDefault();
            if (best?.Payload is null)
            {
                return null;
            }

            // Qdrant already applied score_threshold, but re-checking the
            // documents-version staleness guard client-side keeps that
            // invariant enforced in exactly one place regardless of what the
            // query filtered on.
            if (best.Payload.DocumentsVersion != documentsVersion)
            {
                return null;
            }

            return new CachedAnswer
            {
                AnswerText = best.Payload.AnswerText,
                Citations = best.Payload.Citations,
                DocumentsVersion = best.Payload.DocumentsVersion,
                CreatedAtUtc = best.Payload.CreatedAtUtc
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    private async Task StoreSemanticAsync(
        string question,
        ReadOnlyMemory<float> questionEmbedding,
        string answerText,
        IReadOnlyList<DocumentChunk> citations,
        long documentsVersion,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCollectionAsync(questionEmbedding.Length, cancellationToken).ConfigureAwait(false);

            var point = new AnswerCachePointStruct
            {
                Id = Guid.NewGuid().ToString(),
                Vector = questionEmbedding.ToArray(),
                Payload = new AnswerCachePayload
                {
                    Question = question,
                    AnswerText = answerText,
                    Citations = citations,
                    DocumentsVersion = documentsVersion,
                    CreatedAtUtc = createdAt
                }
            };

            using var response = await _httpClient
                .PutAsJsonAsync($"/collections/{_options.CollectionName}/points?wait=true",
                    new AnswerCacheUpsertRequest { Points = [point] }, cancellationToken)
                .ConfigureAwait(false);
            _ = response; // best-effort — a failed cache write must not fail an already-successful answer.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
        }
    }

    private async Task EnsureCollectionAsync(int dimensions, CancellationToken cancellationToken)
    {
        using var existing = await _httpClient.GetAsync($"/collections/{_options.CollectionName}", cancellationToken).ConfigureAwait(false);
        if (existing.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        var request = new CreateCollectionRequest
        {
            Vectors = new VectorParams { Size = dimensions, Distance = DistanceMetric }
        };

        using var response = await _httpClient
            .PutAsJsonAsync($"/collections/{_options.CollectionName}", request, cancellationToken)
            .ConfigureAwait(false);
        _ = response; // best-effort — see StoreSemanticAsync.
    }
}
