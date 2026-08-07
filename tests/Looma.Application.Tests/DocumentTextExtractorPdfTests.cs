using Looma.Application.Extraction;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Builds a real PDF at test time via PdfPig's own writer API (rather than
/// committing a binary fixture file) and round-trips it through
/// <see cref="DocumentTextExtractor"/> — verifies actual PdfPig usage, not
/// just that some method exists.
/// </summary>
public sealed class DocumentTextExtractorPdfTests : IDisposable
{
    private readonly string _tempPdfPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.pdf");

    [Fact]
    public async Task ExtractAsync_Pdf_RecoversTextFromRealDocument()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(400, 400);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Looma uses Qdrant as its vector database.", 12, new PdfPoint(25, 350), font);

        await File.WriteAllBytesAsync(_tempPdfPath, builder.Build());

        var text = await DocumentTextExtractor.ExtractAsync(_tempPdfPath);

        Assert.Contains("Looma", text);
        Assert.Contains("Qdrant", text);
    }

    [Fact]
    public void IsSupported_Pdf_ReturnsTrue()
    {
        Assert.True(DocumentTextExtractor.IsSupported(".pdf"));
        Assert.True(DocumentTextExtractor.IsSupported(".PDF"));
    }

    public void Dispose()
    {
        if (File.Exists(_tempPdfPath))
        {
            File.Delete(_tempPdfPath);
        }
    }
}
