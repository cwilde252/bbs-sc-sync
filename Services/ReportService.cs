using Microsoft.Extensions.Logging;
using SafetyCultureSync.Models;

namespace SafetyCultureSync.Services;

public sealed class ReportService
{
    private readonly ILogger<ReportService> _logger;

    public ReportService(ILogger<ReportService> logger)
    {
        _logger = logger;
    }

    public void Print(SyncReport report, bool dryRun)
    {
        var mode = dryRun ? " [DRY-RUN]" : "";

        _logger.LogInformation("─────────────────────────────────────────────");
        _logger.LogInformation("Standort-Sync abgeschlossen{Mode}", mode);
        _logger.LogInformation("Gesellschaft : {Name} ({Id})",
            report.GesellschaftName, report.GesellschaftId);
        _logger.LogInformation("Verarbeitet  : {Total}", report.Total);
        _logger.LogInformation("  Angelegt   : {Created}", report.Created);
        _logger.LogInformation("  Übersprungen (bereits vorhanden): {Skipped}", report.Skipped);
        _logger.LogInformation("  Name-Abweichungen (nur gewarnt)  : {Drifts}", report.NameDrifts);
        _logger.LogInformation("  Fehler     : {Failed}", report.Failed);
        _logger.LogInformation("─────────────────────────────────────────────");

        if (report.Failed > 0)
            _logger.LogWarning("{Count} Filialen konnten nicht angelegt werden — Logfile prüfen.",
                report.Failed);

        if (report.NameDrifts > 0)
            _logger.LogWarning(
                "{Count} Name-Abweichungen erkannt. Manuelle Prüfung empfohlen.", report.NameDrifts);

        if (dryRun)
            _logger.LogInformation(
                "DRY-RUN: Kein Schreibzugriff erfolgt. " +
                "Zum echten Lauf: --Sync:DryRun=false übergeben.");
    }

    public void PrintResponseSet(ResponseSetSyncReport report, bool dryRun)
    {
        var mode = dryRun ? " [DRY-RUN]" : "";

        _logger.LogInformation("─────────────────────────────────────────────");
        _logger.LogInformation("Response-Set-Sync abgeschlossen{Mode}", mode);
        _logger.LogInformation("Response Set : {Name} ({Id})",
            report.ResponseSetName, report.ResponseSetId);
        _logger.LogInformation("Verarbeitet  : {Total}", report.Total);
        _logger.LogInformation("  Hinzugefügt                      : {Added}", report.Added);
        _logger.LogInformation("  Übersprungen (bereits vorhanden) : {Skipped}", report.Skipped);
        _logger.LogInformation("  Übersprungen (keine Erweiterung) : {SkippedNoExt}", report.SkippedNoExt);
        _logger.LogInformation("  Fehler                           : {Failed}", report.Failed);
        _logger.LogInformation("─────────────────────────────────────────────");

        if (report.Failed > 0)
            _logger.LogWarning(
                "{Count} Antworten konnten nicht hinzugefügt werden — Logfile prüfen.",
                report.Failed);

        if (dryRun)
            _logger.LogInformation(
                "DRY-RUN: Keine Antworten wurden tatsächlich hinzugefügt.");
    }
}
