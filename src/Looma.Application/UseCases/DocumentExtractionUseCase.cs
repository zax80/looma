using Looma.Application.Extraction;

namespace Looma.Application.UseCases;

/// <summary>
/// Bridges a <see cref="Stream"/> to <see cref="DocumentTextExtractor"/>,
/// which only knows how to read from a file path (it's built for the
/// indexing pipeline, where files already live on disk). Writes the
/// stream to a temp file, extracts, deletes the temp file — deliberately
/// simple rather than adding stream-based overloads to three extractors
/// (PdfPig/OpenXml both support opening from a Stream directly, so that's
/// a viable future optimization, but not needed for a single ad-hoc chat
/// attachment).
/// </summary>
public sealed class DocumentExtractionUseCase : IDocumentExtractionUseCase
{
    public async Task<string> ExtractAsync(Stream documentStream, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);
        if (!DocumentTextExtractor.IsSupported(extension))
        {
            throw new NotSupportedException(
                $"'{extension}' isn't a supported document type. Supported: " +
                string.Join(", ", DocumentTextExtractor.SupportedExtensions));
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
        try
        {
            await using (var fileStream = File.Create(tempFilePath))
            {
                await documentStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            return await DocumentTextExtractor.ExtractAsync(tempFilePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(tempFilePath);
            }
            catch (IOException)
            {
                // Best-effort cleanup — same "don't fail the whole
                // operation over a stray temp file" spirit as MainPage's
                // own recording-file cleanup after voice input.
            }
        }
    }
}
