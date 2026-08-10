using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Looma.Application.DocumentGeneration;

/// <summary>
/// Builds a minimal .docx from plain text via the Open XML SDK — same
/// library <see cref="Looma.Application.Extraction.DocxTextExtractor"/>
/// uses to read one, just in reverse. One paragraph per line (blank lines
/// become blank paragraphs, giving basic visual spacing); a bold title
/// heading first if one was given. Deliberately simple — no styles,
/// headers/footers, or rich formatting; the source is an LLM chat answer's
/// plain text, which has none of that to preserve.
/// </summary>
internal static class DocxDocumentWriter
{
    public static byte[] Write(string title, string content)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            if (!string.IsNullOrWhiteSpace(title))
            {
                var titleRun = new Run(new Text(title) { Space = SpaceProcessingModeValues.Preserve });
                titleRun.PrependChild(new RunProperties(new Bold(), new FontSize { Val = "32" }));
                body.AppendChild(new Paragraph(titleRun));
                body.AppendChild(new Paragraph()); // blank line separating the title from the body
            }

            foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
            {
                var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
                body.AppendChild(new Paragraph(run));
            }
        }

        return stream.ToArray();
    }
}
