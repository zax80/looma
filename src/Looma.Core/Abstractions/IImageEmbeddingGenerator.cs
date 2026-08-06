namespace Looma.Core.Abstractions;

/// <summary>
/// Local CLIP-space image embedding (e.g. open_clip ViT-B/32 via ONNX
/// Runtime). Kept distinct from
/// <c>Microsoft.Extensions.AI.IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>,
/// which is text-only — image and text embeddings must never be produced
/// through the same abstraction or land in the same collection.
/// </summary>
public interface IImageEmbeddingGenerator
{
    Task<ReadOnlyMemory<float>> EmbedAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default);
}
