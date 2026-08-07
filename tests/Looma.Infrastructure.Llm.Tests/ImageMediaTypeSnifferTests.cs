using Looma.Infrastructure.Llm.Vision;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

public sealed class ImageMediaTypeSnifferTests
{
    [Fact]
    public void Detect_PngMagicBytes_ReturnsImagePng()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        Assert.Equal("image/png", ImageMediaTypeSniffer.Detect(png, ".jpg")); // extension deliberately wrong — content wins
    }

    [Fact]
    public void Detect_JpegMagicBytes_ReturnsImageJpeg()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

        Assert.Equal("image/jpeg", ImageMediaTypeSniffer.Detect(jpeg, ".png")); // extension deliberately wrong — content wins
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".JPG", "image/jpeg")]
    public void Detect_UnrecognizedBytes_FallsBackToExtension(string extension, string expected)
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03];

        Assert.Equal(expected, ImageMediaTypeSniffer.Detect(garbage, extension));
    }

    [Fact]
    public void Detect_UnrecognizedBytesAndExtension_Throws()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03];

        Assert.Throws<NotSupportedException>(() => ImageMediaTypeSniffer.Detect(garbage, ".bmp"));
    }

    [Fact]
    public void Detect_EmptyBytes_FallsBackToExtension()
    {
        Assert.Equal("image/png", ImageMediaTypeSniffer.Detect(ReadOnlySpan<byte>.Empty, ".png"));
    }
}
