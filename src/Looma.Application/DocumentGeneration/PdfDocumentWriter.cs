using SkiaSharp;

namespace Looma.Application.DocumentGeneration;

/// <summary>
/// Builds a minimal paginated PDF from plain text via SkiaSharp's
/// <see cref="SKDocument.CreatePdf(System.IO.Stream)"/> — the same package
/// <c>Looma.Infrastructure.Llm</c> already depends on for CLIP image
/// preprocessing (see <c>Directory.Packages.props</c>), reused here rather
/// than pulling in a dedicated third-party PDF-writing library (QuestPDF,
/// PdfSharpCore, ...): one fewer license/dependency to review, and this
/// project already has real, working precedent for depending on document-
/// format libraries directly from <c>Looma.Application</c> (see
/// <see cref="DocxDocumentWriter"/>, PdfPig for reading).
///
/// Same deliberately-simple scope as <see cref="DocxDocumentWriter"/>: one
/// serif-ish sans body font, word-wrapped plain paragraphs, a bold title
/// first if one was given, paginating (new PDF page) whenever content would
/// run past the bottom margin. No headers/footers/page numbers/rich
/// formatting — the source is an LLM chat answer's plain text, which has
/// none of that to preserve.
/// </summary>
internal static class PdfDocumentWriter
{
    // US Letter, in PDF points (1 pt = 1/72 inch) — SKDocument.CreatePdf's
    // own unit, per its remarks. A4 users lose a little width; not worth a
    // config knob for chat-answer-length content.
    private const float PageWidth = 612f;
    private const float PageHeight = 792f;
    private const float Margin = 54f; // 0.75"
    private const float TitleFontSize = 20f;
    private const float BodyFontSize = 12f;
    private const float LineHeight = BodyFontSize * 1.35f;

    public static byte[] Write(string title, string content)
    {
        using var stream = new MemoryStream();

        using (var document = SKDocument.CreatePdf(stream))
        {
            using var titleFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), TitleFontSize);
            using var bodyFont = new SKFont(SKTypeface.FromFamilyName("Arial"), BodyFontSize);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

            var maxTextWidth = PageWidth - (2 * Margin);
            var canvas = document.BeginPage(PageWidth, PageHeight);
            var y = Margin + LineHeight;

            if (!string.IsNullOrWhiteSpace(title))
            {
                foreach (var titleLine in WrapLine(title, titleFont, maxTextWidth))
                {
                    y = EnsureRoom(document, ref canvas, y, LineHeight * 1.5f);
                    canvas.DrawText(titleLine, Margin, y, SKTextAlign.Left, titleFont, paint);
                    y += LineHeight * 1.5f;
                }

                y += LineHeight * 0.5f; // blank line separating the title from the body
            }

            foreach (var paragraph in content.Replace("\r\n", "\n").Split('\n'))
            {
                if (paragraph.Length == 0)
                {
                    y = EnsureRoom(document, ref canvas, y, LineHeight);
                    y += LineHeight; // blank paragraph -> blank line, same as DocxDocumentWriter
                    continue;
                }

                foreach (var line in WrapLine(paragraph, bodyFont, maxTextWidth))
                {
                    y = EnsureRoom(document, ref canvas, y, LineHeight);
                    canvas.DrawText(line, Margin, y, SKTextAlign.Left, bodyFont, paint);
                    y += LineHeight;
                }
            }

            document.EndPage();
            document.Close();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Starts a fresh page (ending the current one first) when
    /// <paramref name="y"/> plus the next line's height would run past the
    /// bottom margin, resetting <paramref name="canvas"/> to the new page's
    /// canvas. Returns the (possibly reset) y to draw the next line at.
    /// </summary>
    private static float EnsureRoom(SKDocument document, ref SKCanvas canvas, float y, float neededHeight)
    {
        if (y + neededHeight <= PageHeight - Margin)
        {
            return y;
        }

        document.EndPage();
        canvas = document.BeginPage(PageWidth, PageHeight);
        return Margin + LineHeight;
    }

    /// <summary>
    /// Greedy word-wrap against <paramref name="maxWidth"/> using the font's
    /// own text measurement — simple but correct for left-to-right text, no
    /// external line-breaking library needed for this use case. A single
    /// word wider than <paramref name="maxWidth"/> on its own is left as-is
    /// (SkiaSharp will just clip/overflow it) rather than force-breaking mid
    /// word, which would need character-level measurement for little
    /// practical benefit here.
    /// </summary>
    private static IEnumerable<string> WrapLine(string text, SKFont font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var currentLine = string.Empty;
        foreach (var word in words)
        {
            var candidate = currentLine.Length == 0 ? word : $"{currentLine} {word}";
            if (font.MeasureText(candidate) <= maxWidth || currentLine.Length == 0)
            {
                currentLine = candidate;
                continue;
            }

            yield return currentLine;
            currentLine = word;
        }

        if (currentLine.Length > 0)
        {
            yield return currentLine;
        }
    }
}
