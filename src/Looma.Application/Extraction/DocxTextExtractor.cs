using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Looma.Application.Extraction;

/// <summary>Extracts plain text from a .docx via the Open XML SDK, one line per paragraph.</summary>
internal static class DocxTextExtractor
{
    public static string ExtractText(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var paragraphs = body.Descendants<Paragraph>()
            .Select(paragraph => paragraph.InnerText)
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return string.Join("\n", paragraphs);
    }
}
