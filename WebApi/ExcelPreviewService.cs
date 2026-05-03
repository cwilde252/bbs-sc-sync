using DevExpress.Spreadsheet;

namespace SafetyCultureSync.WebApi;

public record WorksheetPreview(
    int Index,
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public record WorkbookPreview(IReadOnlyList<WorksheetPreview> Worksheets);

public sealed class ExcelPreviewService
{
    private const int PreviewRowCount = 50;

    public WorkbookPreview GetPreview(Stream excelStream)
    {
        using var workbook = new Workbook();
        workbook.LoadDocument(excelStream, DocumentFormat.Xlsx);

        var worksheets = new List<WorksheetPreview>(workbook.Worksheets.Count);

        for (int i = 0; i < workbook.Worksheets.Count; i++)
        {
            var sheet   = workbook.Worksheets[i];
            var headers = ReadHeaders(sheet);
            var rows    = ReadRows(sheet, headers.Count);
            worksheets.Add(new WorksheetPreview(i, sheet.Name, headers, rows));
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
            headers.Add(string.IsNullOrEmpty(value) ? ColumnLetter(c) : value);
        }

        return headers;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadRows(Worksheet sheet, int colCount)
    {
        var usedRange = sheet.GetUsedRange();
        if (usedRange == null)
            return [];

        int lastRow = Math.Min(usedRange.BottomRowIndex, PreviewRowCount); // Zeilen 1–10
        var rows = new List<IReadOnlyList<string>>(lastRow);

        for (int r = 1; r <= lastRow; r++)
        {
            var cells = new List<string>(colCount);
            for (int c = 0; c < colCount; c++)
                cells.Add(sheet[r, c].Value.ToString().Trim());
            rows.Add(cells);
        }

        return rows;
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
