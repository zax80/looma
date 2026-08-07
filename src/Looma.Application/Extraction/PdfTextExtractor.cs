using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Looma.Application.Extraction;

/// <summary>
/// Extracts plain text from a PDF via PdfPig. Pages are joined with a blank
/// line between them so <see cref="Chunking.TextChunker"/>'s line-based
/// citation metadata still lands on something meaningful, even though a
/// "line" here is really "a line within a page's extracted text", not a
/// literal PDF page number — see <see cref="DocumentTextExtractor"/>.
/// </summary>
internal static class PdfTextExtractor
{
    public static string ExtractText(string filePath)
    {
        using var document = PdfDocument.Open(filePath);

        var pageTexts = new List<string>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            // Per PdfPig's own guidance: page.Text preserves internal content
            // stream order, which is frequently not reading order.
            // ContentOrderTextExtractor is what they recommend specifically
            // for RAG/LLM use.
            pageTexts.Add(ContentOrderTextExtractor.GetText(page));
        }

        return string.Join("\n\n", pageTexts);
    }
}
