namespace Looma.Core.Abstractions;

/// <summary>
/// Embeds natural-language TEXT into the same CLIP vector space
/// <see cref="IImageEmbeddingGenerator"/> embeds images into — the paired
/// text tower of the same CLIP checkpoint, so a query embedded here is
/// directly comparable (cosine similarity) against vectors already stored
/// in the <c>images</c> collection. This exists specifically for
/// text→image search ("photos of a sunset") — the natural-language
/// counterpart to <see cref="IImageEmbeddingGenerator"/>'s image-to-image
/// search.
///
/// This is the one deliberate, narrow exception to
/// <see cref="IImageEmbeddingGenerator"/>'s "never mix image and text
/// embeddings" warning: the output here still only ever lands in the
/// <c>images</c> collection, alongside CLIP image vectors — never
/// conflated with, or interchangeable with,
/// <c>Microsoft.Extensions.AI.IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>'s
/// (unrelated, differently-dimensioned) document-text embeddings used for
/// the <c>documents</c> collection.
/// </summary>
public interface ITextToImageEmbeddingGenerator
{
    Task<ReadOnlyMemory<float>> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);
}
