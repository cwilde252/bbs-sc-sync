# SafetyCulture Standort-Sync

C# .NET 10 Konsolenanwendung zum automatisierten Upload von Filialdaten aus Excel in SafetyCulture.

## Voraussetzungen

- .NET 10 SDK
- DevExpress-Lizenz (NuGet-Feed konfiguriert)
- SafetyCulture Premium oder Enterprise Plan
- API-Token mit Recht `Platform management: Sites`

## Konfiguration

**API-Key** — niemals in `appsettings.json`. Stattdessen:

```bash
# Windows
set SAFETYCULTURE__APIKEY=dein-token-hier

# Linux / macOS
export SAFETYCULTURE__APIKEY=dein-token-hier
```

**DevExpress NuGet-Feed** in `nuget.config` (im Repo-Root):

```xml
<configuration>
  <packageSources>
    <add key="DevExpress" value="https://nuget.devexpress.com/your-feed-url/api" />
  </packageSources>
</configuration>
```

## Verwendung

```bash
# 1. Erst immer Dry-Run
dotnet run -- --gesellschaft "Lidl DE"

# 2. Nach Prüfung des Outputs: echter Lauf
dotnet run -- --gesellschaft "Lidl DE" --Sync:DryRun=false

# Andere Gesellschaft / andere Datei
dotnet run -- --gesellschaft "Lidl AT" --Sync:ExcelFilePath ./filialen_at.xlsx --Sync:DryRun=false

# Gesellschaft automatisch anlegen ohne Bestätigung
dotnet run -- --gesellschaft "Lidl DE" --Sync:ForceCreate=true --Sync:DryRun=false
```

## Excel-Format

| Spalte | Header                    |
|--------|---------------------------|
| A (0)  | `Filial-Nr`               |
| B (1)  | `Straße`                  |
| C (2)  | `PLZ`                     |
| D (3)  | `Ort`                     |
| E (4)  | `Namens-Erweiterung-Lidl` |

Zeile 1 = Header (wird übersprungen). Leere `Filial-Nr` → Zeile wird ignoriert.

## Exit-Codes

| Code | Bedeutung                                      |
|------|------------------------------------------------|
| 0    | Erfolgreich (auch wenn Einträge übersprungen)  |
| 1    | Teilfehler (mind. 1 Filiale konnte nicht angelegt werden) |
| 2    | Durch User abgebrochen                         |
| 3    | Excel-Datei nicht gefunden                     |
| 4    | API-Key fehlt                                  |
| 99   | Unerwarteter Fehler                            |

## Projektstruktur

```
SafetyCultureSync/
├── Program.cs                  Entry Point, DI, CLI-Args
├── appsettings.json            Konfiguration
├── Models/
│   └── Models.cs               ExcelLocation, DirectoryFolder, SyncReport
├── Services/
│   ├── ExcelReaderService.cs   DevExpress Excel-Parsing
│   ├── SafetyCultureClient.cs  API-Client (GET/POST /directory/v1/)
│   ├── SyncOrchestrator.cs     Abgleich-Logik
│   └── ReportService.cs        Ausgabe
└── Infrastructure/
    ├── AppConfiguration.cs     Options-Klassen
    └── HttpRetryHandler.cs     Polly Retry (429, 5xx)
```
