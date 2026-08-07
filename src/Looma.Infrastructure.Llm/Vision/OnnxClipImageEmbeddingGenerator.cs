using Looma.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Looma.Infrastructure.Llm.Vision;

/// <summary>
/// CLIP ViT-B/32 image embedding via a local ONNX Runtime session — no
/// network call, no Ollama involved (Ollama doesn't serve ONNX vision
/// encoders). See <c>docs/model-setup.md</c> for where to get the
/// <c>clip-vit-b32.onnx</c> file this points at
/// (<c>Models.ImageEmbeddingModel.ModelPath</c> in config.json).
///
/// Deliberately does not hardcode the model's input/output tensor names —
/// different published CLIP ONNX exports name them differently (e.g.
/// "pixel_values" vs "input"), and guessing wrong would fail in a way
/// that's only discoverable by actually running it. Takes whatever single
/// input/output the session reports instead.
/// </summary>
public sealed class OnnxClipImageEmbeddingGenerator : IImageEmbeddingGenerator, IDisposable
{
    private readonly Lazy<InferenceSession> _session;
    private readonly string _modelPath;

    public OnnxClipImageEmbeddingGenerator(string modelPath)
    {
        _modelPath = modelPath;

        // Lazy, not eager in the constructor: this type is registered as a
        // DI singleton and constructed at startup regardless of whether the
        // run ever touches an image — loading the ONNX model (and failing
        // loudly if it's missing) should happen on first real use, not
        // block every CLI invocation on a file that might not be needed.
        _session = new Lazy<InferenceSession>(CreateSession);
    }

    /// <summary>
    /// Synchronous under the hood — the ONNX Runtime C# API doesn't expose
    /// an async <c>Run</c> overload, so this blocks the calling thread for
    /// the duration of inference. Still returns <c>Task</c> to satisfy
    /// <see cref="IImageEmbeddingGenerator"/>, which other implementations
    /// (a remote embedding service, say) might genuinely need to await.
    /// </summary>
    public Task<ReadOnlyMemory<float>> EmbedAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var buffer = new MemoryStream();
        imageStream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        var pixelData = ClipImagePreprocessor.Preprocess(bytes);
        var inputTensor = new DenseTensor<float>(
            pixelData,
            [1, 3, ClipImagePreprocessor.TargetSize, ClipImagePreprocessor.TargetSize]);

        var session = _session.Value;
        var inputName = session.InputMetadata.Keys.First();

        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, inputTensor)]);
        var output = results.First().AsEnumerable<float>().ToArray();

        return Task.FromResult<ReadOnlyMemory<float>>(L2Normalize(output));
    }

    /// <summary>
    /// CLIP embeddings are compared via cosine similarity, which is
    /// scale-invariant, so normalizing here is technically redundant if the
    /// exported model already does it internally — but not every published
    /// export does, and re-normalizing an already-unit vector is a no-op, so
    /// this is done unconditionally rather than trying to detect which case
    /// applies.
    /// </summary>
    private static float[] L2Normalize(float[] vector)
    {
        var sumSquares = 0.0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        if (norm <= float.Epsilon)
        {
            return vector;
        }

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / norm;
        }

        return normalized;
    }

    private InferenceSession CreateSession()
    {
        if (!File.Exists(_modelPath))
        {
            throw new FileNotFoundException(
                $"CLIP ONNX model not found at '{_modelPath}' (Models.ImageEmbeddingModel.ModelPath in config.json). " +
                "See docs/model-setup.md for how to obtain it.",
                _modelPath);
        }

        return new InferenceSession(_modelPath);
    }

    public void Dispose()
    {
        if (_session.IsValueCreated)
        {
            _session.Value.Dispose();
        }
    }
}
