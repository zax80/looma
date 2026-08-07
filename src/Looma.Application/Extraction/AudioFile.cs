namespace Looma.Application.Extraction;

/// <summary>
/// Extension check for the audio ingestion path, mirroring
/// <see cref="ImageFile"/>'s shape. Both formats go through
/// <c>IAudioTranscriber</c> in <c>IndexingUseCase</c> — there's no separate
/// "extractor" the way text has one, since audio always needs a real model
/// call (transcription), never just a parsing library.
/// </summary>
public static class AudioFile
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".wav", ".mp3"];

    public static bool IsSupported(string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
