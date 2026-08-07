using System.Runtime.CompilerServices;
using Looma.Application.Configuration;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Looma.Application.UseCases;

public sealed class SearchUseCase : ISearchUseCase
{
    private readonly IVectorStore _vectorStore;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly RagOptions _ragOptions;

    public SearchUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<RagOptions> ragOptions)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _ragOptions = ragOptions.Value;
    }

    public async IAsyncEnumerable<VectorSearchResult> SearchAsync(
        string query,
        VectorCollection collection = VectorCollection.Documents,
        int topK = 5,
        float? minRelevanceScore = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var threshold = minRelevanceScore ?? _ragOptions.MinRelevanceScore;

        await foreach (var result in _vectorStore.SearchAsync(
            collection, queryEmbedding, topK, threshold, cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }
    }
}
