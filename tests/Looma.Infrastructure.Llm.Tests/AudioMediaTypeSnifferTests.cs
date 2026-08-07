using Looma.Infrastructure.Llm.Audio;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

public sealed class AudioMediaTypeSnifferTests
{
    [Fact]
    public void Detect_WavMagicBytes_ReturnsWav()
    {
        // "RIFF" + 4 arbitrary chunk-size bytes (irrelevant to detection) + "WAVE"
        byte[] wav = [(byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E'];

        Assert.Equal(AudioFormat.Wav, AudioMediaTypeSniffer.Detect(wav, ".mp3")); // extension deliberately wrong — content wins
    }

    [Fact]
    public void Detect_Id3TaggedMp3_ReturnsMp3()
    {
        byte[] mp3 = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0];

        Assert.Equal(AudioFormat.Mp3, AudioMediaTypeSniffer.Detect(mp3, ".wav")); // extension deliberately wrong — content wins
    }

    [Fact]
    public void Detect_RawMpegFrameSyncMp3_ReturnsMp3()
    {
        byte[] mp3 = [0xFF, 0xFB, 0x90, 0x00, 0x00];

        Assert.Equal(AudioFormat.Mp3, AudioMediaTypeSniffer.Detect(mp3, ".wav"));
    }

    [Theory]
    [InlineData(".wav", AudioFormat.Wav)]
    [InlineData(".mp3", AudioFormat.Mp3)]
    [InlineData(".WAV", AudioFormat.Wav)]
    public void Detect_UnrecognizedBytes_FallsBackToExtension(string extension, AudioFormat expected)
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03];

        Assert.Equal(expected, AudioMediaTypeSniffer.Detect(garbage, extension));
    }

    [Fact]
    public void Detect_UnrecognizedBytesAndExtension_Throws()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03];

        Assert.Throws<NotSupportedException>(() => AudioMediaTypeSniffer.Detect(garbage, ".ogg"));
    }

    [Fact]
    public void Detect_EmptyBytes_FallsBackToExtension()
    {
        Assert.Equal(AudioFormat.Mp3, AudioMediaTypeSniffer.Detect(ReadOnlySpan<byte>.Empty, ".mp3"));
    }
}
