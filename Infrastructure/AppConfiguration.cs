namespace SafetyCultureSync.Infrastructure;

/// <summary>
/// Dynamisches Spalten-Mapping und Lese-Optionen für den Web-Upload-Endpunkt.
/// </summary>
public record ExcelReadOptions(
    int SheetIndex,
    /// <summary>0-basierter Zeilenindex der ersten Datenzeile (z. B. 1 = zweite Zeile = nach Header)</summary>
    int StartRow,
    int FilialNrCol,
    int StrasseCol,
    int PlzCol,
    int OrtCol,
    int NamensErweiterungCol);


public sealed class SafetyCultureOptions
{
    public const string Section = "SafetyCulture";

    public string ApiKey           { get; init; } = string.Empty;
    public string BaseUrl          { get; init; } = "https://api.safetyculture.io/";
    public int    RateLimitDelayMs { get; init; } = 200;
}

public sealed class SyncOptions
{
    public const string Section = "Sync";

    public string ExcelFilePath { get; init; } = "./data/filialen.xlsx";
    public bool   DryRun        { get; init; } = true;
    public int    SheetIndex    { get; init; } = 0;
    public bool   ForceCreate   { get; init; } = false;
    public string AreaLabel { get; init; } = "area";
    public string ResponseSetName { get; init; } = "";
}
