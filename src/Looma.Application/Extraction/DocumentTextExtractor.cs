namespace Looma.Application.Extraction;

/// <summary>
/// Single entry point <see cref="Looma.Application.UseCases.IndexingUseCase"/> uses to turn a file on
/// disk into plain text, regardless of format. Extends the original
/// text-only milestone (.txt/.md/.csv, read directly) with real PDF/DOCX/XLSX
/// parsing — PdfPig and the Open XML SDK respectively — per the decision to
/// use those two libraries rather than shelling out to an external converter
/// or reading raw bytes as text (CLAUDE.md: never truncate/fake extraction).
///
/// Image and audio ingestion (captioning/OCR, transcription) are still
/// deliberately out of scope here — those need a model call, not just a
/// parsing library, and land with Whisper/vision-captioning per the brief's
/// milestone order.
/// </summary>
public static class DocumentTextExtractor
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".txt", ".md", ".csv", ".pdf", ".docx", ".xlsx"];

    public static bool IsSupported(string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts plain text from <paramref name="filePath"/>. Throws for an
    /// unsupported extension — callers are expected to check
    /// <see cref="IsSupported"/> first (see <see cref="Looma.Application.UseCases.IndexingUseCase"/>,
    /// which reports unsupported extensions as <c>Skipped</c> rather than
    /// ever reaching this call).
    /// </summary>
    public static async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath);

        return extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".csv" => await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false),
            ".pdf" => PdfTextExtractor.ExtractText(filePath),
            ".docx" => DocxTextExtractor.ExtractText(filePath),
            ".xlsx" => XlsxTextExtractor.ExtractText(filePath),
            _ => throw new NotSupportedException(
                $"No text extractor registered for '{extension}'. Supported: {string.Join(", ", SupportedExtensions)}.")
        };
    }
}
