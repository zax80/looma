namespace Looma.Application.Configuration;

/// <summary>
/// Binds to the <c>Models:ImageEmbeddingModel</c> section of config.json.
/// Same rationale as <see cref="EmbeddingModelOptions"/> — Application needs
/// only the vector dimensionality (512 for CLIP ViT-B/32) to call
/// <c>IVectorStore.EnsureCollectionAsync(VectorCollection.Images, ...)</c>;
/// everything else about the model (provider, ONNX model path) is an
/// Infrastructure.Llm concern.
/// </summary>
public sealed class ImageEmbeddingModelOptions
{
    public const string SectionName = "Models:ImageEmbeddingModel";

    public int Dimensions { get; set; } = 512;
}
