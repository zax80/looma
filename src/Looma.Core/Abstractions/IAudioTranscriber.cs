using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>Local speech-to-text (e.g. Whisper via ONNX). Streams segments as they're produced.</summary>
public interface IAudioTranscriber
{
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        Stream audioStream,
        CancellationToken cancellationToken = default);
}
