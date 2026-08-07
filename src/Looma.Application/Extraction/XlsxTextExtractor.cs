using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Looma.Application.Extraction;

/// <summary>
/// Extracts plain text from a .xlsx via the Open XML SDK: every sheet, every
/// row, cells tab-separated, rows newline-separated. Shared strings are
/// resolved (a cell's raw value is just an index into the workbook's shared
/// string table when <c>DataType == SharedString</c>) — skipping that would
/// silently extract numbers instead of the actual text.
/// </summary>
internal static class XlsxTextExtractor
{
    public static string ExtractText(string filePath)
    {
        using var document = SpreadsheetDocument.Open(filePath, isEditable: false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return string.Empty;
        }

        var sharedStrings = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault()?.SharedStringTable;

        var rowTexts = new List<string>();
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            if (worksheetPart.Worksheet is not { } worksheet)
            {
                // Defensive, not expected in practice: a worksheet part with
                // no parsed Worksheet would mean a malformed/unreadable
                // sheet — skip it rather than throwing away the whole file.
                continue;
            }

            foreach (var row in worksheet.Descendants<Row>())
            {
                var cellTexts = row.Elements<Cell>()
                    .Select(cell => GetCellText(cell, sharedStrings))
                    .Where(text => !string.IsNullOrEmpty(text));

                var rowText = string.Join("\t", cellTexts);
                if (!string.IsNullOrWhiteSpace(rowText))
                {
                    rowTexts.Add(rowText);
                }
            }
        }

        return string.Join("\n", rowTexts);
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var rawValue = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(rawValue))
        {
            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(rawValue, out var sharedStringIndex))
        {
            return sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(sharedStringIndex)?.InnerText ?? string.Empty;
        }

        return rawValue;
    }
}
