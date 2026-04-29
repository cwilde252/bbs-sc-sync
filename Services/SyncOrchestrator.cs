using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SafetyCultureSync.Infrastructure;
using SafetyCultureSync.Models;

namespace SafetyCultureSync.Services;

/// <summary>
/// Orchestriert den vollständigen Sync-Ablauf:
/// 1. Gesellschaft auflösen — muss bereits in SafetyCulture existieren
/// 2. meta_label der Filial-Ebene aus dem Gesellschafts-Folder ableiten
/// 3. Bestehende Child-Folders indizieren (Key = FilialNr)
/// 4. Excel-Zeilen abgleichen → CREATE | SKIP | WARN
/// </summary>
public sealed class SyncOrchestrator
{
    private readonly SafetyCultureClient _client;
    private readonly SyncOptions _opts;
    private readonly ILogger<SyncOrchestrator> _logger;

    private static readonly Regex FilialNrPattern =
        new(@"^(\d+)\s*-", RegexOptions.Compiled);

    public SyncOrchestrator(
        SafetyCultureClient client,
        IOptions<SyncOptions> opts,
        ILogger<SyncOrchestrator> logger)
    {
        _client = client;
        _opts = opts.Value;
        _logger = logger;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public async Task<SyncReport> RunAsync(
        IReadOnlyList<ExcelLocation> locations,
        string gesellschaftName,
        bool? dryRunOverride = null,
        CancellationToken ct = default)
    {
        var isDryRun = dryRunOverride ?? _opts.DryRun;
        if (isDryRun)
            _logger.LogWarning("=== DRY-RUN MODUS — keine API-Schreibzugriffe ===");

        // 1. Gesellschaft-Folder suchen — muss existieren
        var gesellschaft = await FindGesellschaftAsync(gesellschaftName, isDryRun, ct);

        // Optionaler Override für meta_label (z. B. wenn API kein MetaLabel liefert)
        if (_opts.AreaLabel is not null)
        {
            _logger.LogWarning(
                "meta_label Override aktiv: '{Override}' (API-Wert war: '{Api}')",
                _opts.AreaLabel, gesellschaft.MetaLabel ?? "<null>");
            gesellschaft = gesellschaft with { MetaLabel = _opts.AreaLabel };
        }

        _logger.LogInformation(
            "Gesellschaft: '{Name}' (ID: {Id}, meta_label: {Label})",
            gesellschaft.Name, gesellschaft.Id, gesellschaft.MetaLabel ?? "<unbekannt>");

        // 2. meta_label der Filial-Ebene aus dem Gesellschafts-Folder ableiten
        var filialMetaLabel = "location";
        _logger.LogInformation("Filial-Ebene meta_label: {Label}", filialMetaLabel);

        // 3. Existing Child-Folders laden und per FilialNr indizieren
        var children = await _client.GetChildFoldersAsync(gesellschaft.Id, ct);
        var index = children
            .Select(f => (Folder: f, Nr: ExtractFilialNr(f.Name)))
            .Where(x => x.Nr is not null)
            .ToDictionary(x => x.Nr!, x => x.Folder);

        _logger.LogInformation("Bestehende Filialen unter Gesellschaft: {Count}", index.Count);

        // 4. Abgleich
        var report = new SyncReport
        {
            GesellschaftId = gesellschaft.Id,
            GesellschaftName = gesellschaft.Name
        };

        foreach (var loc in locations)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessLocationAsync(loc, gesellschaft, filialMetaLabel, index, report, isDryRun, ct);
        }

        return report;
    }

    // ─── Gesellschaft auflösen ────────────────────────────────────────────────

    private async Task<DirectoryFolder> FindGesellschaftAsync(
        string name, bool isDryRun, CancellationToken ct)
    {
        _logger.LogInformation("Suche Gesellschaft: '{Name}'", name);

        var allFolders = await _client.GetAllFoldersAsync(ct);
        var match = allFolders.FirstOrDefault(
                            f => Normalize(f.Name) == Normalize(name));

        if (match is not null)
        {
            _logger.LogInformation("Gesellschaft gefunden (ID: {Id})", match.Id);
            return match;
        }

        // Nicht gefunden — Anlage nur möglich wenn meta_label bekannt
        if (_opts.AreaLabel is null)
        {
            var rootFolders = allFolders
                .Where(f => string.IsNullOrEmpty(f.ParentId))
                .Select(f => $"  '{f.Name}' (meta_label: {f.MetaLabel})")
                .ToList();

            var hint = rootFolders.Count > 0
                ? $"\nVorhandene Root-Folder:\n{string.Join("\n", rootFolders)}"
                : "\nKeine Root-Folder gefunden.";

            throw new InvalidOperationException(
                $"Gesellschaft '{name}' nicht gefunden.{hint}\n\n" +
                $"Zum automatischen Anlegen meta_label übergeben:\n" +
                $"  --Sync:GesellschaftMetaLabel=level_1");
        }

        // meta_label bekannt → anlegen
        _logger.LogWarning(
            "Gesellschaft '{Name}' nicht gefunden. Lege an mit meta_label: {Label}",
            name, _opts.AreaLabel);

        if (!_opts.ForceCreate)
        {
            Console.Write($"\nGesellschaft '{name}' mit meta_label '{_opts.AreaLabel}' anlegen? (j/n): ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer != "j")
                throw new OperationCanceledException("Abbruch durch User.");
        }

        if (isDryRun)
        {
            _logger.LogInformation(
                "[DRY-RUN] Würde Gesellschaft anlegen: '{Name}' (meta_label: {Label})",
                name, _opts.AreaLabel);
            return new DirectoryFolder("dry-run-gesellschaft-id", name, null, _opts.AreaLabel);
        }

        var created = await _client.CreateFolderAsync(
            name, parentId: null, _opts.AreaLabel, ct);
        _logger.LogInformation(
            "Gesellschaft angelegt: '{Name}' (ID: {Id})", created.Name, created.Id);
        return created;
    }

    // ─── Einzelne Filiale verarbeiten ─────────────────────────────────────────

    private async Task ProcessLocationAsync(
        ExcelLocation loc,
        DirectoryFolder gesellschaft,
        string filialMetaLabel,
        Dictionary<string, DirectoryFolder> index,
        SyncReport report,
        bool isDryRun,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(loc.FilialNr))
        {
            _logger.LogWarning("Ungültige Zeile: FilialNr leer → übersprungen.");
            report.Failed++;
            return;
        }

        if (index.TryGetValue(loc.FilialNr, out var existing))
        {
            if (existing.Name != loc.SiteName)
            {
                _logger.LogWarning(
                    "Name-Abweichung [{Nr}]: SafetyCulture='{Ist}'  Excel='{Soll}'",
                    loc.FilialNr, existing.Name, loc.SiteName);
                report.NameDrifts++;
            }
            else
            {
                _logger.LogDebug("Übersprungen (bereits vorhanden): {Name}", loc.SiteName);
            }

            report.Skipped++;
            return;
        }

        if (isDryRun)
        {
            _logger.LogInformation(
                "[DRY-RUN] Würde anlegen: '{Name}' unter '{Gesellschaft}' (meta_label: {Label})",
                loc.SiteName, gesellschaft.Name, filialMetaLabel);
            report.Created++;
            return;
        }

        try
        {
            var created = await _client.CreateFolderAsync(
                loc.SiteName, parentId: gesellschaft.Id, filialMetaLabel, ct);

            _logger.LogInformation(
                "Angelegt: '{Name}'", loc.SiteName);

            index[loc.FilialNr] = created;
            report.Created++;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Fehler beim Anlegen von '{Name}': {Message}", loc.SiteName, ex.Message);
            report.Failed++;
        }
    }

    // ─── Hilfsmethoden ────────────────────────────────────────────────────────

    /// <summary>
    /// Bestimmt das meta_label der Kind-Ebene direkt aus dem Parent-meta_label.
    /// "level_1" → "level_2", "level_2" → "level_3", usw.
    /// </summary>
    private static string ResolveChildMetaLabel(string? parentMetaLabel)
    {
        if (parentMetaLabel is null)
            return "level_2";

        var numStr = parentMetaLabel.Replace("level_", "");
        return int.TryParse(numStr, out var num)
            ? $"level_{num + 1}"
            : "level_2";
    }

    /// <summary>
    /// Extrahiert die FilialNr aus einem Folder-Namen.
    /// "10001 - Berlin (Nord)" → "10001"
    /// </summary>
    private static string? ExtractFilialNr(string name)
    {
        var match = FilialNrPattern.Match(name);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Normalisiert für Vergleiche: Trim + Lowercase + Sonderzeichen zu Leerzeichen.
    /// </summary>
    private static string Normalize(string s) =>
        s.Trim().ToLowerInvariant()
         .Replace("-", " ")
         .Replace("_", " ");
}
