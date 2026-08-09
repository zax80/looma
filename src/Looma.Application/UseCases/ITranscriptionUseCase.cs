namespace Looma.Application.UseCases;

/// <summary>
/// Ad-hoc speech-to-text for a single audio clip (e.g. a chat voice-input
/// recording) — NOT indexing: no chunking, no embedding, no storage, just
/// text back. Contrast with the indexing pipeline's own audio handling,
/// which goes through <c>TranscriptChunker</c> and the vector store.
/// </summary>
public interface ITranscriptionUseCase
{
    /// <summary>Concatenates every segment Whisper produces into one string, space-separated.</summary>
    Task<string> TranscribeAsync(Stream audioStream, CancellationToken cancellationToken = default);
}
