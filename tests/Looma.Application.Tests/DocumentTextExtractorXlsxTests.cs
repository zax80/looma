using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Looma.Application.Extraction;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Builds a real .xlsx at test time via the Open XML SDK's own writer API —
/// specifically using the shared-string table, since that's the codepath
/// real-world Excel-authored text cells actually use (see
/// <c>XlsxTextExtractor.GetCellText</c>'s shared-string resolution) — and
/// round-trips it through <see cref="DocumentTextExtractor"/>.
/// </summary>
public sealed class DocumentTextExtractorXlsxTests : IDisposable
{
    private readonly string _tempXlsxPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.xlsx");

    [Fact]
    public async Task ExtractAsync_Xlsx_ResolvesSharedStringCellText()
    {
        using (var document = SpreadsheetDocument.Create(_tempXlsxPath, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new Text("Looma uses Qdrant as its vector database.")));
            sharedStringPart.SharedStringTable.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData(
                new Row(
                    new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") }));
            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Sheet1"
            });

            workbookPart.Workbook.Save();
        }

        var text = await DocumentTextExtractor.ExtractAsync(_tempXlsxPath);

        Assert.Contains("Looma", text);
        Assert.Contains("Qdrant", text);
    }

    [Fact]
    public void IsSupported_Xlsx_ReturnsTrue()
    {
        Assert.True(DocumentTextExtractor.IsSupported(".xlsx"));
    }

    public void Dispose()
    {
        if (File.Exists(_tempXlsxPath))
        {
            File.Delete(_tempXlsxPath);
        }
    }
}
