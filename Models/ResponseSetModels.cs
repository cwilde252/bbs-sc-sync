using System.Text.Json.Serialization;

namespace SafetyCultureSync.Models;

// ─── List Response Sets ───────────────────────────────────────────────────────

public record GlobalResponseSet(
    [property: JsonPropertyName("responseset_id")] string Id,
    [property: JsonPropertyName("name")] string Name);

public record ResponseSetListResponse(
    [property: JsonPropertyName("response_sets")] List<GlobalResponseSet> ResponseSets,
    [property: JsonPropertyName("next_page_token")] string? NextPageToken
);

// ─── Response Set Detail (einzelne Antworten) ─────────────────────────────────

public record GrsResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label
);

public record ResponseSetDetailResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("responses")] List<GrsResponse> Responses
);

// ─── Report ───────────────────────────────────────────────────────────────────

public class ResponseSetSyncReport
{
    public string ResponseSetId { get; init; } = string.Empty;
    public string ResponseSetName { get; init; } = string.Empty;
    public int Added { get; set; }
    public int Skipped { get; set; }   // bereits vorhanden
    public int SkippedNoExt { get; set; }   // keine NamensErweiterung
    public int Failed { get; set; }

    public int Total => Added + Skipped + SkippedNoExt + Failed;
}
