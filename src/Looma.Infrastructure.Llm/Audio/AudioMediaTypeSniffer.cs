namespace Looma.Infrastructure.Llm.Audio;

/// <summary>
/// Detects the real audio container format from magic bytes rather than
/// trusting a file extension — mirrors <c>Vision.ImageMediaTypeSniffer</c>'s
/// reasoning exactly. <see cref="IAudioTranscriber.TranscribeAsync"/> only
/// gets a bare <see cref="Stream"/>, no extension, so this is the only
/// signal available for choosing between <see cref="NAudio.Wave.WaveFileReader"/>
/// and <see cref="NAudio.Wave.Mp3FileReader"/>.
/// </summary>
public static class AudioMediaTypeSniffer
{
    public static AudioFormat Detect(ReadOnlySpan<byte> bytes, string fallbackExtension)
    {
        // RIFF....WAVE — the standard WAV container header.
        if (bytes.Length >= 12 &&
            bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E')
        {
            return AudioFormat.Wav;
        }

        // MP3: either an ID3v2 tag at the start, or a raw MPEG frame sync
        // (11 set bits: 0xFF followed by a byte with its top 3 bits set).
        if (bytes.Length >= 3 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
        {
            return AudioFormat.Mp3;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0)
        {
            return AudioFormat.Mp3;
        }

        return fallbackExtension.ToLowerInvariant() switch
        {
            ".wav" => AudioFormat.Wav,
            ".mp3" => AudioFormat.Mp3,
            _ => throw new NotSupportedException(
                $"Could not determine an audio format from content or extension '{fallbackExtension}'.")
        };
    }
}
