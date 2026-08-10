namespace Looma.Application.UseCases;

/// <summary>
/// Ad-hoc text extraction for a single attached document (e.g. a chat
/// attachment) — NOT indexing: no chunking, no embedding, no storage, just
/// text back. Contrast with the indexing pipeline's own document handling
/// (<c>Looma.Application.Extraction.DocumentTextExtractor</c>), which this
/// reuses under the hood but never pushes through <c>TextChunker</c> or the
/// vector store. Same "ask about it live, don't index it" shape as
/// <see cref="ITranscriptionUseCase"/>/<see cref="IImageCaptionUseCase"/>.
/// </summary>
public interface IDocumentExtractionUseCase
{
    /// <param name="documentStream">The document's raw bytes.</param>
    /// <param name="fileName">
    /// Original file name — only its extension is used, to pick the right
    /// extractor (same set <c>DocumentTextExtractor.SupportedExtensions</c>
    /// covers: .txt/.md/.csv/.pdf/.docx/.xlsx). Throws
    /// <see cref="NotSupportedException"/> for anything else.
    /// </param>
    Task<string> ExtractAsync(Stream documentStream, string fileName, CancellationToken cancellationToken = default);
}
