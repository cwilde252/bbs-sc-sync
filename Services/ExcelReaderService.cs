using DevExpress.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SafetyCultureSync.Infrastructure;
using SafetyCultureSync.Models;

namespace SafetyCultureSync.Services;

/// <summary>
/// Liest Filialdaten aus einer Excel-Datei (.xlsx) via DevExpress.Document.Processing.
/// Spalten-Mapping (0-basiert):
///   0 = Filial-Nr  |  1 = Straße  |  2 = PLZ  |  3 = Ort  |  4 = Namens-Erweiterung-Lidl
/// </summary>
public sealed class ExcelReaderService
{
    private readonly SyncOptions                _opts;
    private readonly ILogger<ExcelReaderService> _logger;

    // Spalten-Indizes als Konstanten — bei Formatänderung hier anpassen
    private static class Col
    {
        public const int FilialNr          = 0;
        public const int Strasse           = 2;
        public const int Plz               = 3;
        public const int Ort               = 4;
        public const int NamensErweiterung = 1;
    }

    public ExcelReaderService(
        IOptions<SyncOptions> opts,
        ILogger<ExcelReaderService> logger)
    {
        _opts   = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Liest alle gültigen Zeilen aus der konfigurierten Excel-Datei.
    /// Gibt leere Zeilen (keine FilialNr) ohne Exception aus.
    /// </summary>
    public IReadOnlyList<ExcelLocation> Read(string? overridePath = null)
    {
        var path = overridePath ?? _opts.ExcelFilePath;

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Excel-Datei nicht gefunden: '{path}'. " +
                "Pfad in appsettings.json (Sync:ExcelFilePath) oder --Sync:ExcelFilePath prüfen.");

        _logger.LogInformation("Lese Excel-Datei: {Path}", path);

        using var workbook = new Workbook();
        workbook.LoadDocument(path, DocumentFormat.Xlsx);

        var sheet     = workbook.Worksheets[_opts.SheetIndex];
        var usedRange = sheet.GetUsedRange();
        int lastRow   = usedRange.BottomRowIndex;

        var locations = new List<ExcelLocation>();
        int skipped   = 0;

        // Zeile 0 = Header → ab Zeile 1 lesen
        for (int r = 1; r <= lastRow; r++)
        {
            var filialNr = sheet[r, Col.FilialNr].Value.ToString().Trim();

            if (string.IsNullOrEmpty(filialNr))
            {
                skipped++;
                _logger.LogDebug("Zeile {Row}: FilialNr leer → übersprungen.", r + 1);
                continue;
            }

            locations.Add(new ExcelLocation(
                FilialNr:          filialNr,
                Strasse:           sheet[r, Col.Strasse].Value.ToString().Trim(),
                Plz:               sheet[r, Col.Plz].Value.ToString().Trim(),
                Ort:               sheet[r, Col.Ort].Value.ToString().Trim(),
                NamensErweiterung: sheet[r, Col.NamensErweiterung].Value.ToString().Trim()
            ));
        }

        _logger.LogInformation(
            "Excel eingelesen: {Count} Filialen, {Skipped} leere Zeilen übersprungen.",
            locations.Count, skipped);

        return locations;
    }
}
