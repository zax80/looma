using Looma.Application.Extraction;
using Xunit;

namespace Looma.Application.Tests;

public sealed class AudioFileTests
{
    [Theory]
    [InlineData(".wav")]
    [InlineData(".mp3")]
    [InlineData(".WAV")]
    [InlineData(".MP3")]
    public void IsSupported_KnownAudioExtensions_ReturnsTrue(string extension)
    {
        Assert.True(AudioFile.IsSupported(extension));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".png")]
    [InlineData("")]
    public void IsSupported_NonAudioExtensions_ReturnsFalse(string extension)
    {
        Assert.False(AudioFile.IsSupported(extension));
    }
}
