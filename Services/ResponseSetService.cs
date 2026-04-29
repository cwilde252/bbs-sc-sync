using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SafetyCultureSync.Infrastructure;
using SafetyCultureSync.Models;

namespace SafetyCultureSync.Services;

/// <summary>
/// Synchronisiert Filialen als Antworten in ein Global Response Set.
///
/// Antwort-Format:  "{FilialNr}_{NamensErweiterung}"  z. B. "10001_Nord"
/// Zeilen ohne NamensErweiterung werden mit Hinweis übersprungen.
///
/// Sicherheitsprinzip: Nur POST einzelner neuer Einträge.
/// Kein GET-all + PUT-replace → bestehende Antworten werden nie gelöscht.
///
/// Endpunkte:
///   GET  /response_sets                     → alle Sets (paginiert)
///   GET  /response_sets/{id}                → Detail mit Responses
///   POST /response_sets/{id}/responses      → einzelne Antwort anhängen
/// </summary>
public sealed class ResponseSetService
{
    // Endpunkt-Pfade als Konstanten — bei 404 hier anpassen
    private const string ListEndpoint = "response_sets";
    private const string DetailEndpoint = "response_sets/{0}";
    private const string AddEndpoint = "response_sets/{0}/responses";

    private readonly HttpClient _http;
    private readonly SyncOptions _opts;
    private readonly ILogger<ResponseSetService> _logger;

    public ResponseSetService(
        HttpClient http,
        IOptions<SyncOptions> opts,
        ILogger<ResponseSetService> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public async Task<ResponseSetSyncReport> RunAsync(
        IReadOnlyList<ExcelLocation> locations,
        string responseSetName,
        bool? dryRunOverride = null,
        CancellationToken ct = default)
    {
        // 1. Response Set per Name finden
        var responseSet = await FindByNameAsync(responseSetName, ct);
        _logger.LogInformation(
            "Response Set: '{Name}' (ID: {Id})", responseSet.Name, responseSet.Id);

        // 2. Bestehende Antworten als HashSet laden (O(1)-Lookup, case-insensitive)
        var existing = await GetExistingLabelsAsync(responseSet.Id, ct);
        _logger.LogInformation(
            "Bestehende Antworten im Set: {Count}", existing.Count);

        var isDryRun = dryRunOverride ?? _opts.DryRun;
        var report = new ResponseSetSyncReport
        {
            ResponseSetId = responseSet.Id,
            ResponseSetName = responseSet.Name
        };

        // 3. Pro Zeile: Antwort-String bauen und abgleichen
        foreach (var loc in locations)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessLocationAsync(loc, responseSet.Id, existing, report, isDryRun, ct);
        }

        return report;
    }

    // ─── Response Set suchen ─────────────────────────────────────────────────

    private async Task<GlobalResponseSet> FindByNameAsync(
    string name, CancellationToken ct)
    {
        _logger.LogInformation("Suche Response Set: '{Name}'", name);

        // API gibt direkt ein Array zurück — kein Wrapper, keine Paginierung
        var allSets = await _http.GetFromJsonAsync<List<GlobalResponseSet>>(ListEndpoint, ct)
                      ?? throw new InvalidOperationException(
                             "SafetyCulture API lieferte leere Antwort bei GET response_sets.");

        var match = allSets.FirstOrDefault(
            s => s.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        var hint = allSets.Count > 0
            ? $"\nVorhandene Response Sets:\n{string.Join("\n", allSets.Select(s => $"  '{s.Name}'"))}"
            : "\nKeine Response Sets gefunden.";

        throw new InvalidOperationException(
            $"Response Set '{name}' nicht gefunden.{hint}\n\n" +
            $"Parameter: --response-set \"<exakter Name>\"");
    }

    // ─── Bestehende Antworten laden ───────────────────────────────────────────

    private async Task<HashSet<string>> GetExistingLabelsAsync(
        string responseSetId, CancellationToken ct)
    {
        var url = string.Format(DetailEndpoint, responseSetId);
        var detail = await _http.GetFromJsonAsync<ResponseSetDetailResponse>(url, ct)
                     ?? throw new InvalidOperationException(
                            $"Leere Antwort bei GET response_sets/{responseSetId}.");

        // Case-insensitiv — doppelte Antworten die sich nur in Groß-/Kleinschreibung
        // unterscheiden sollen ebenfalls als Duplikat gewertet werden
        return new HashSet<string>(
            detail.Responses.Select(r => r.Label),
            StringComparer.OrdinalIgnoreCase);
    }

    // ─── Einzelne Filiale verarbeiten ─────────────────────────────────────────

    private async Task ProcessLocationAsync(
        ExcelLocation loc,
        string responseSetId,
        HashSet<string> existing,
        ResponseSetSyncReport report,
        bool isDryRun,
        CancellationToken ct)
    {
        // Zeilen ohne NamensErweiterung: Hinweis + überspringen
        if (string.IsNullOrWhiteSpace(loc.NamensErweiterung))
        {
            _logger.LogInformation(
                "Übersprungen (keine NamensErweiterung): FilialNr {Nr}", loc.FilialNr);
            report.SkippedNoExt++;
            return;
        }

        var label = $"{loc.FilialNr}{loc.NamensErweiterung}";

        // Duplikat prüfen
        if (existing.Contains(label))
        {
            _logger.LogDebug("Bereits vorhanden: '{Label}'", label);
            report.Skipped++;
            return;
        }

        // Neu hinzufügen
        if (isDryRun)
        {
            _logger.LogInformation("[DRY-RUN] Würde hinzufügen: '{Label}'", label);
            existing.Add(label); // verhindert doppelte DryRun-Logs bei duplizierten Excel-Zeilen
            report.Added++;
            return;
        }

        try
        {
            var url = string.Format(AddEndpoint, responseSetId);
            var payload = new { label };
            var response = await _http.PostAsJsonAsync(url, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Antwort-Anlage fehlgeschlagen [{(int)response.StatusCode}]: {body}");
            }

            _logger.LogInformation("Hinzugefügt: '{Label}'", label);
            existing.Add(label); // Index aktuell halten
            report.Added++;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Fehler beim Hinzufügen von '{Label}': {Message}", label, ex.Message);
            report.Failed++;
        }
    }
}
