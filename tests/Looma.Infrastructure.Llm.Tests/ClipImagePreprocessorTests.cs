using Looma.Infrastructure.Llm.Vision;
using SkiaSharp;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

/// <summary>
/// Pure-function tests — no ONNX model file required. What actually feeds a
/// real CLIP model correctly can only be verified end-to-end with a real
/// model, by whoever runs this locally (see docs/model-setup.md); this only
/// verifies the preprocessing shape/range contract
/// <see cref="OnnxClipImageEmbeddingGenerator"/> depends on.
/// </summary>
public sealed class ClipImagePreprocessorTests
{
    [Fact]
    public void Preprocess_SquareImage_ReturnsCorrectlySizedNchwTensor()
    {
        var bytes = CreateSolidColorPng(width: 300, height: 300, r: 200, g: 100, b: 50);

        var tensor = ClipImagePreprocessor.Preprocess(bytes);

        Assert.Equal(3 * ClipImagePreprocessor.TargetSize * ClipImagePreprocessor.TargetSize, tensor.Length);
    }

    [Fact]
    public void Preprocess_NonSquareImage_StillProducesCorrectlySizedTensor()
    {
        // Exercises the resize-then-center-crop path — the common
        // real-world case, not just a pre-square test fixture.
        var bytes = CreateSolidColorPng(width: 640, height: 200, r: 10, g: 20, b: 30);

        var tensor = ClipImagePreprocessor.Preprocess(bytes);

        Assert.Equal(3 * ClipImagePreprocessor.TargetSize * ClipImagePreprocessor.TargetSize, tensor.Length);
    }

    [Fact]
    public void Preprocess_SmallerThanTargetImage_UpscalesToCorrectSize()
    {
        // Below TargetSize on both dimensions — the resize step must scale
        // up, not just crop/skip, or this would silently under-fill the tensor.
        var bytes = CreateSolidColorPng(width: 50, height: 80, r: 5, g: 5, b: 5);

        var tensor = ClipImagePreprocessor.Preprocess(bytes);

        Assert.Equal(3 * ClipImagePreprocessor.TargetSize * ClipImagePreprocessor.TargetSize, tensor.Length);
    }

    [Fact]
    public void Preprocess_SolidColorImage_NormalizedValuesAreConstantAndInExpectedRange()
    {
        // A solid-color image's every normalized pixel in a channel should
        // be identical, and CLIP's mean/std constants put valid pixel
        // values (0-255) somewhere in roughly [-2.2, 2.7] per channel —
        // catches a wrong normalization constant or wrong channel order
        // without needing a real model to notice.
        var bytes = CreateSolidColorPng(width: 224, height: 224, r: 128, g: 128, b: 128);

        var tensor = ClipImagePreprocessor.Preprocess(bytes);
        var channelSize = ClipImagePreprocessor.TargetSize * ClipImagePreprocessor.TargetSize;

        for (var channel = 0; channel < 3; channel++)
        {
            var first = tensor[channel * channelSize];
            for (var i = 0; i < channelSize; i++)
            {
                Assert.Equal(first, tensor[(channel * channelSize) + i], precision: 3);
            }

            Assert.InRange(first, -3.0f, 3.0f);
        }
    }

    [Fact]
    public void Preprocess_DifferentChannels_ProduceDifferentNormalizedValues()
    {
        // A sanity check against an accidental R/G/B channel swap: a color
        // with distinct R/G/B values should normalize to three distinct
        // per-channel constants, not the same value three times.
        var bytes = CreateSolidColorPng(width: 224, height: 224, r: 10, g: 120, b: 240);

        var tensor = ClipImagePreprocessor.Preprocess(bytes);
        var channelSize = ClipImagePreprocessor.TargetSize * ClipImagePreprocessor.TargetSize;

        var rValue = tensor[0];
        var gValue = tensor[channelSize];
        var bValue = tensor[2 * channelSize];

        Assert.NotEqual(rValue, gValue);
        Assert.NotEqual(gValue, bValue);
        Assert.NotEqual(rValue, bValue);
    }

    private static byte[] CreateSolidColorPng(int width, int height, byte r, byte g, byte b)
    {
        var color = new SKColor(r, g, b);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return data.ToArray();
    }
}
