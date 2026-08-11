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
    private readonly ITextToImageEmbeddingGenerator? _textToImageEmbeddingGenerator;
    private readonly RagOptions _ragOptions;

    /// <param name="textToImageEmbeddingGenerator">
    /// Optional — see <see cref="ISearchUseCase"/>'s doc comment. Null when
    /// <c>Models.ImageEmbeddingModel.TextTower</c> isn't configured;
    /// <see cref="VectorCollection.Documents"/> queries don't need it at
    /// all, so its absence never blocks anything except an images-
    /// collection text query specifically.
    /// </param>
    public SearchUseCase(
        IVectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<RagOptions> ragOptions,
        ITextToImageEmbeddingGenerator? textToImageEmbeddingGenerator = null)
    {
        _vectorStore = vectorStore;
        _embeddingGenerator = embeddingGenerator;
        _ragOptions = ragOptions.Value;
        _textToImageEmbeddingGenerator = textToImageEmbeddingGenerator;
    }

    public async IAsyncEnumerable<VectorSearchResult> SearchAsync(
        string query,
        VectorCollection collection = VectorCollection.Documents,
        int topK = 5,
        float? minRelevanceScore = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queryEmbedding = collection == VectorCollection.Images
            ? await EmbedForImageSearchAsync(query, cancellationToken).ConfigureAwait(false)
            : await _embeddingGenerator.GenerateVectorAsync(query, cancellationToken: cancellationToken).ConfigureAwait(false);

        var threshold = minRelevanceScore ?? _ragOptions.MinRelevanceScore;

        await foreach (var result in _vectorStore.SearchAsync(
            collection, queryEmbedding, topK, threshold, cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }
    }

    private async Task<ReadOnlyMemory<float>> EmbedForImageSearchAsync(string query, CancellationToken cancellationToken)
    {
        if (_textToImageEmbeddingGenerator is null)
        {
            throw new InvalidOperationException(
                "Text→image search isn't configured — Models.ImageEmbeddingModel.TextTower is missing " +
                "from config.json. See docs/model-setup.md's \"Text→image search\" section.");
        }

        return await _textToImageEmbeddingGenerator.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
