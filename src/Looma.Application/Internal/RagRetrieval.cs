using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.Internal;

/// <summary>
/// The "embed a query, search the documents collection, shape the results
/// into citations" step shared by <see cref="Looma.Application.UseCases.AnswerUseCase"/>
/// and <see cref="Looma.Application.UseCases.ChatUseCase"/>. Extracted here
/// so the two don't silently drift apart on exactly how a citation is
/// built — AnswerUseCase itself is left untouched (already verified,
/// no reason to risk it) and only ChatUseCase calls this.
/// </summary>
internal static class RagRetrieval
{
    public static async Task<List<DocumentChunk>> RetrieveCitationsAsync(
        IVectorStore vectorStore,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        float minRelevanceScore,
        CancellationToken cancellationToken)
    {
        var citations = new List<DocumentChunk>();
        await foreach (var result in vectorStore
            .SearchAsync(VectorCollection.Documents, queryEmbedding, topK, minRelevanceScore, cancellationToken)
            .ConfigureAwait(false))
        {
            citations.Add(new DocumentChunk
            {
                Id = result.Id,
                SourceId = result.Metadata.SourcePath,
                Content = result.Content ?? string.Empty,
                Metadata = result.Metadata
            });
        }

        return citations;
    }
}
