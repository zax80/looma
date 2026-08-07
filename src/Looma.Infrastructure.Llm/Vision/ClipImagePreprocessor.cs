using SkiaSharp;

namespace Looma.Infrastructure.Llm.Vision;

/// <summary>
/// Turns raw image bytes into the exact tensor layout CLIP ViT-B/32 expects:
/// resize-then-center-crop to a fixed square, per-channel normalized with
/// CLIP's own mean/std (not ImageNet's — they differ), laid out NCHW
/// (channel-major) as a flat array. This is the standard OpenAI/open_clip
/// preprocessing recipe (see e.g. the <c>preprocessor_config.json</c>
/// shipped alongside published CLIP ONNX exports) — not invented here, just
/// implemented directly since there's no vetted preprocessing NuGet package
/// to depend on instead.
///
/// Uses SkiaSharp (MIT-licensed) rather than SixLabors.ImageSharp — the
/// first attempt used ImageSharp, but its 4.x line requires a Six Labors
/// license key even at build time (a real build failure, not a hypothetical
/// concern), which is a non-starter for a dependency this deep in the
/// pipeline.
///
/// Kept as a pure function (bytes in, floats out) specifically so it's
/// testable without a real ONNX model file — the actual inference step
/// (<see cref="OnnxClipImageEmbeddingGenerator"/>) is the one piece of this
/// pipeline that can only be verified against a real model, by the person
/// running it locally.
/// </summary>
public static class ClipImagePreprocessor
{
    public const int TargetSize = 224;

    // CLIP's own per-channel normalization constants (RGB order) — distinct
    // from the ImageNet defaults ([0.485, 0.456, 0.406] / [0.229, 0.224, 0.225])
    // that a lot of other vision models use. Using the wrong pair silently
    // produces embeddings that "work" (no exception, right shape) but are
    // subtly wrong — worth calling out explicitly rather than leaving as a
    // bare magic-number array.
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    /// <summary>
    /// Returns a flat NCHW float array of length <c>3 * TargetSize * TargetSize</c>
    /// (channel, then row, then column) ready to wrap in a
    /// <c>DenseTensor&lt;float&gt;</c> with shape <c>[1, 3, TargetSize, TargetSize]</c>.
    /// </summary>
    public static float[] Preprocess(byte[] imageBytes)
    {
        using var original = SKBitmap.Decode(imageBytes)
            ?? throw new InvalidOperationException("Could not decode image bytes — unrecognized or corrupt image format.");

        using var cropped = ResizeAndCenterCrop(original, TargetSize);

        var channelSize = TargetSize * TargetSize;
        var tensor = new float[3 * channelSize];

        for (var y = 0; y < TargetSize; y++)
        {
            for (var x = 0; x < TargetSize; x++)
            {
                var color = cropped.GetPixel(x, y);
                var offset = (y * TargetSize) + x;
                tensor[(0 * channelSize) + offset] = ((color.Red / 255f) - Mean[0]) / Std[0];
                tensor[(1 * channelSize) + offset] = ((color.Green / 255f) - Mean[1]) / Std[1];
                tensor[(2 * channelSize) + offset] = ((color.Blue / 255f) - Mean[2]) / Std[2];
            }
        }

        return tensor;
    }

    /// <summary>
    /// Resizes so the shorter side becomes <paramref name="targetSize"/>
    /// (preserving aspect ratio), then center-crops the longer side down to
    /// match — the textbook CLIP preprocessing recipe, done as two explicit
    /// steps since SkiaSharp doesn't have a single "resize into a crop box"
    /// call the way ImageSharp's <c>ResizeMode.Crop</c> does.
    /// </summary>
    private static SKBitmap ResizeAndCenterCrop(SKBitmap source, int targetSize)
    {
        var scale = (float)targetSize / Math.Min(source.Width, source.Height);
        var scaledWidth = Math.Max(targetSize, (int)MathF.Round(source.Width * scale));
        var scaledHeight = Math.Max(targetSize, (int)MathF.Round(source.Height * scale));

        using var resized = source.Resize(
            new SKImageInfo(scaledWidth, scaledHeight),
            new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("SkiaSharp failed to resize the image.");

        var cropX = (scaledWidth - targetSize) / 2;
        var cropY = (scaledHeight - targetSize) / 2;
        var subset = new SKRectI(cropX, cropY, cropX + targetSize, cropY + targetSize);

        var cropped = new SKBitmap(targetSize, targetSize);
        if (!resized.ExtractSubset(cropped, subset))
        {
            cropped.Dispose();
            throw new InvalidOperationException("SkiaSharp failed to center-crop the resized image.");
        }

        return cropped;
    }
}
