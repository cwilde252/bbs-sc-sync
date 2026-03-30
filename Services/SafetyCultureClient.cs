using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SafetyCultureSync.Models;

namespace SafetyCultureSync.Services;

/// <summary>
/// HTTP-Client-Wrapper für die SafetyCulture Directory API v1.
/// Alle "Sites" in der SafetyCulture-UI entsprechen "Folders" in der REST-API.
/// Basis-URL: https://api.safetyculture.io/directory/v1/
/// Erforderliches Token-Recht: "Platform management: Sites"
/// </summary>
public sealed class SafetyCultureClient
{
    private readonly HttpClient                   _http;
    private readonly ILogger<SafetyCultureClient> _logger;

    public SafetyCultureClient(
        HttpClient http,
        ILogger<SafetyCultureClient> logger)
    {
        _http   = http;
        _logger = logger;
    }

    // ─── READ ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ruft alle Folders der Organisation ab (vollständige Liste, paginiert).
    /// Jeder Folder enthält bereits sein meta_label — kein separater Labels-Endpunkt nötig.
    /// </summary>
    public async Task<List<DirectoryFolder>> GetAllFoldersAsync(
        CancellationToken ct = default)
    {
        var folders   = new List<DirectoryFolder>();
        string? token = null;
        int pageNr    = 0;

        do
        {
            pageNr++;
            var url = token is null
                ? "directory/v1/folders?page_size=100"
                : $"directory/v1/folders?page_size=100&page_token={token}";

            _logger.LogDebug("GET {Url} (Seite {Page})", url, pageNr);

            var resp = await _http.GetFromJsonAsync<FolderListResponse>(url, ct)
                       ?? throw new InvalidOperationException(
                              "SafetyCulture API lieferte leere Antwort bei GET folders.");

            folders.AddRange(resp.Folders);
            token = resp.NextPageToken;


        } while (!string.IsNullOrEmpty(token));

        _logger.LogDebug("GetAllFolders: {Count} Folders auf {Pages} Seiten.", folders.Count, pageNr);
        return folders;
    }

    /// <summary>
    /// Ruft alle direkten Kind-Folders eines Parent-Folders ab.
    /// </summary>
    public async Task<List<DirectoryFolder>> GetChildFoldersAsync(
        string parentId, CancellationToken ct = default)
    {
        var folders   = new List<DirectoryFolder>();
        string? token = null;

        do
        {
            var url = token is null
                ? $"directory/v1/folders?page_size=100&parent_id={Uri.EscapeDataString(parentId)}"
                : $"directory/v1/folders?page_size=100&parent_id={Uri.EscapeDataString(parentId)}&page_token={token}";

            var resp = await _http.GetFromJsonAsync<FolderListResponse>(url, ct)
                       ?? throw new InvalidOperationException(
                              $"Leere Antwort bei GET child folders (parent: {parentId}).");

            folders.AddRange(resp.Folders);
            token = resp.NextPageToken;

        } while (!string.IsNullOrEmpty(token));

        return folders;
    }

    // ─── WRITE ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Legt einen neuen Folder an.
    /// meta_label wird aus dem Parent-Folder abgeleitet (eine Ebene tiefer).
    /// </summary>
    public async Task<DirectoryFolder> CreateFolderAsync(
        string name, string? parentId, string metaLabel, CancellationToken ct = default)
    {
        var payload = new CreateFolderRequest(name, parentId, metaLabel);

        _logger.LogDebug(
            "POST directory/v1/folder  name={Name}  parent_id={Parent}  meta_label={Label}",
            name, parentId ?? "<root>", metaLabel);

        var response = await _http.PostAsJsonAsync("directory/v1/folder", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Folder-Anlage fehlgeschlagen [{(int)response.StatusCode}]: {body}");
        }

        var created = await response.Content.ReadFromJsonAsync<DirectoryFolder>(ct)
                      ?? throw new InvalidOperationException(
                             "SafetyCulture API lieferte leere Antwort bei POST folder.");

        _logger.LogDebug("Folder angelegt: '{Name}' → ID={Id}", created.Name, created.Id);
        return created;
    }
}
