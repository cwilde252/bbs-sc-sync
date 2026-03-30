using System.Text.Json.Serialization;

namespace SafetyCultureSync.Models;

// ─── Excel-Eingabe ────────────────────────────────────────────────────────────

public record ExcelLocation(
    string FilialNr,
    string Strasse,
    string Plz,
    string Ort,
    string NamensErweiterung
)
{
    /// <summary>
    /// Folder-Name in SafetyCulture.
    /// Ohne Erweiterung: "10001 - Berlin"
    /// Mit Erweiterung:  "10001 - Berlin (Nord)"
    /// </summary>
    public string SiteName => $"{FilialNr} {Strasse} {Plz} {Ort}";
}

// ─── SafetyCulture API-Modelle ────────────────────────────────────────────────

public record DirectoryFolder(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent_id")] string? ParentId,
    [property: JsonPropertyName("meta_label")] string? MetaLabel = "area"
);

public record FolderListResponse(
    [property: JsonPropertyName("folders")] List<DirectoryFolder> Folders,
    [property: JsonPropertyName("next_page_token")] string? NextPageToken
);

public record CreateFolderRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent_id")] string? ParentId,
    [property: JsonPropertyName("meta_label")] string MetaLabel
);

// ─── Sync-Ergebnis ────────────────────────────────────────────────────────────

public class SyncReport
{
    public string GesellschaftId { get; init; } = string.Empty;
    public string GesellschaftName { get; init; } = string.Empty;
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int NameDrifts { get; set; }

    public int Total => Created + Skipped + Failed;
}
