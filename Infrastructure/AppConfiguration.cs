namespace SafetyCultureSync.Infrastructure;

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
