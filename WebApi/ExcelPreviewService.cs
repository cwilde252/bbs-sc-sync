using DevExpress.Spreadsheet;

namespace SafetyCultureSync.WebApi;

public record WorksheetPreview(int Index, string Name, IReadOnlyList<string> Headers);
public record WorkbookPreview(IReadOnlyList<WorksheetPreview> Worksheets);

/// <summary>
/// Liest Worksheet-Namen und Header-Zeilen aus einem Excel-Stream (ohne Datenzeilen).
/// </summary>
public sealed class ExcelPreviewService
{
    public WorkbookPreview GetPreview(Stream excelStream)
    {
        using var workbook = new Workbook();
        workbook.LoadDocument(excelStream, DocumentFormat.Xlsx);

        var worksheets = new List<WorksheetPreview>(workbook.Worksheets.Count);

        for (int i = 0; i < workbook.Worksheets.Count; i++)
        {
            var sheet   = workbook.Worksheets[i];
            var headers = ReadHeaders(sheet);
            worksheets.Add(new WorksheetPreview(i, sheet.Name, headers));
        }

        return new WorkbookPreview(worksheets);
    }

    private static IReadOnlyList<string> ReadHeaders(Worksheet sheet)
    {
        var usedRange = sheet.GetUsedRange();
        if (usedRange == null)
            return [];

        int lastCol = usedRange.RightColumnIndex;
        var headers = new List<string>(lastCol + 1);

        for (int c = 0; c <= lastCol; c++)
        {
            var value = sheet[0, c].Value.ToString().Trim();
            // Fallback: Spaltenbuchstabe wenn Header leer
            headers.Add(string.IsNullOrEmpty(value) ? ColumnLetter(c) : value);
        }

        return headers;
    }

    private static string ColumnLetter(int index)
    {
        var result = string.Empty;
        index++;
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index /= 26;
        }
        return result;
    }
}
