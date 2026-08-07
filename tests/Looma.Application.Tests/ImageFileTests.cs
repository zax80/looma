using Looma.Application.Extraction;
using Xunit;

namespace Looma.Application.Tests;

public sealed class ImageFileTests
{
    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".PNG")]
    [InlineData(".JPG")]
    public void IsSupported_KnownImageExtensions_ReturnsTrue(string extension)
    {
        Assert.True(ImageFile.IsSupported(extension));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    [InlineData("")]
    public void IsSupported_NonImageExtensions_ReturnsFalse(string extension)
    {
        Assert.False(ImageFile.IsSupported(extension));
    }
}
