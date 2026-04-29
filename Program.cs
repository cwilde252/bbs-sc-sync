using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using SafetyCultureSync.Infrastructure;
using SafetyCultureSync.Models;
using SafetyCultureSync.Services;
using SafetyCultureSync.WebApi;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

// ── Web-Modus ─────────────────────────────────────────────────────────────────
if (args.Contains("--web"))
    return await RunWebAsync(args);

try
{
    var gesellschaft = GetRequiredArg(args, "--gesellschaft");
    var responseSet = GetOptionalArg(args, "--response-set");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    builder.Services.Configure<SafetyCultureOptions>(
        builder.Configuration.GetSection(SafetyCultureOptions.Section));
    builder.Services.Configure<SyncOptions>(
        builder.Configuration.GetSection(SyncOptions.Section));

    builder.Services.AddTransient<HttpRetryHandler>();

    // Shared HttpClient für alle Services die die SafetyCulture-API aufrufen
    builder.Services.AddHttpClient("SafetyCulture", (sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<SafetyCultureOptions>>().Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "SafetyCulture API-Key fehlt. " +
                "Setze Environment Variable SAFETYCULTURE__APIKEY.");

        client.BaseAddress = new Uri(opts.BaseUrl);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        client.DefaultRequestHeaders.Add("sc-integration-id", "sc-sync-tool");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddHttpMessageHandler<HttpRetryHandler>();

    // Typed Clients über die gemeinsame "SafetyCulture"-Factory
    builder.Services.AddHttpClient<SafetyCultureClient>((sp, client) =>
        ConfigureClient(sp, client))
        .AddHttpMessageHandler<HttpRetryHandler>();

    builder.Services.AddHttpClient<ResponseSetService>((sp, client) =>
        ConfigureClient(sp, client))
        .AddHttpMessageHandler<HttpRetryHandler>();

    builder.Services.AddTransient<ExcelReaderService>();
    builder.Services.AddTransient<SyncOrchestrator>();
    builder.Services.AddTransient<ReportService>();

    var host = builder.Build();

    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var syncOpts = host.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
    var excelReader = host.Services.GetRequiredService<ExcelReaderService>();
    var orchestrator = host.Services.GetRequiredService<SyncOrchestrator>();
    var rsService = host.Services.GetRequiredService<ResponseSetService>();
    var reporter = host.Services.GetRequiredService<ReportService>();

    logger.LogInformation("SafetyCulture Sync gestartet");
    logger.LogInformation("Gesellschaft  : {G}", gesellschaft);
    logger.LogInformation("Response Set  : {R}", responseSet ?? "<nicht angegeben>");
    logger.LogInformation("Excel-Datei   : {F}", syncOpts.ExcelFilePath);
    logger.LogInformation("Dry-Run       : {D}", syncOpts.DryRun);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var locations = excelReader.Read();
    var exitCode = 0;

    // ── 1. Standort-Sync ──────────────────────────────────────────────────────
    var syncReport = await orchestrator.RunAsync(locations, gesellschaft, ct: cts.Token);
    reporter.Print(syncReport, syncOpts.DryRun);
    if (syncReport.Failed > 0) exitCode = 1;

    // ── 2. Response-Set-Sync (optional) ──────────────────────────────────────
    var rsName = responseSet ?? syncOpts.ResponseSetName;
    if (rsName is not null)
    {
        var rsReport = await rsService.RunAsync(locations, rsName, ct: cts.Token);
        reporter.PrintResponseSet(rsReport, syncOpts.DryRun);
        if (rsReport.Failed > 0) exitCode = 1;
    }

    return exitCode;
}
catch (OperationCanceledException ex)
{
    Log.Warning("Abbruch: {Message}", ex.Message);
    return 2;
}
catch (FileNotFoundException ex)
{
    Log.Fatal("Datei nicht gefunden: {Message}", ex.Message);
    return 3;
}
catch (InvalidOperationException ex) when (ex.Message.Contains("API-Key"))
{
    Log.Fatal("{Message}", ex.Message);
    return 4;
}
catch (InvalidOperationException ex) when (ex.Message.Contains("nicht gefunden"))
{
    Log.Fatal("{Message}", ex.Message);
    return 5;
}
catch (ArgumentException ex)
{
    Log.Fatal("{Message}", ex.Message);
    return 6;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unerwarteter Fehler");
    return 99;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// ─── Hilfsmethoden ────────────────────────────────────────────────────────────

static void ConfigureClient(IServiceProvider sp, HttpClient client)
{
    var opts = sp.GetRequiredService<IOptions<SafetyCultureOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", opts.ApiKey);
    client.DefaultRequestHeaders.Add("sc-integration-id", "sc-sync-tool");
    client.Timeout = TimeSpan.FromSeconds(30);
}

static string GetRequiredArg(string[] args, string key)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
            return args[i][(key.Length + 1)..].Trim('"');

        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1].Trim('"');
    }

    throw new ArgumentException(
        $"Pflichtparameter fehlt: {key}\n" +
        $"Verwendung: dotnet run -- {key} \"Lidl DE\"");
}

static string? GetOptionalArg(string[] args, string key)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase))
            return args[i][(key.Length + 1)..].Trim('"');

        if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1].Trim('"');
    }
    return null;
}

// ─── Web-Modus ────────────────────────────────────────────────────────────────

static async Task<int> RunWebAsync(string[] args)
{
    // Filter --web from args so WebApplication.CreateBuilder doesn't choke on it
    var filteredArgs = args.Where(a => a != "--web").ToArray();
    var builder = WebApplication.CreateBuilder(filteredArgs);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    builder.Services.Configure<SafetyCultureOptions>(
        builder.Configuration.GetSection(SafetyCultureOptions.Section));
    builder.Services.Configure<SyncOptions>(
        builder.Configuration.GetSection(SyncOptions.Section));

    builder.Services.AddTransient<HttpRetryHandler>();

    builder.Services.AddHttpClient<SafetyCultureClient>((sp, client) =>
        ConfigureClient(sp, client))
        .AddHttpMessageHandler<HttpRetryHandler>();

    builder.Services.AddHttpClient<ResponseSetService>((sp, client) =>
        ConfigureClient(sp, client))
        .AddHttpMessageHandler<HttpRetryHandler>();

    builder.Services.AddTransient<ExcelReaderService>();
    builder.Services.AddTransient<SyncOrchestrator>();
    builder.Services.AddTransient<ReportService>();
    builder.Services.AddTransient<ExcelPreviewService>();

    var app = builder.Build();

    app.UseStaticFiles();

    // ── GET /api/config ───────────────────────────────────────────────────────
    app.MapGet("/api/config", (IOptions<SyncOptions> syncOpts) =>
        Results.Ok(new { responseSetName = syncOpts.Value.ResponseSetName }));

    // ── GET / → Web-UI ────────────────────────────────────────────────────────
    app.MapGet("/", () => Results.File(
        Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
        contentType: "text/html; charset=utf-8"));

    // ── POST /api/excel/preview ───────────────────────────────────────────────
    app.MapPost("/api/excel/preview", async (
        IFormFile file,
        ExcelPreviewService previewSvc) =>
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest("Keine Datei übergeben.");

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = previewSvc.GetPreview(stream);
            return Results.Ok(preview);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(ex.Message);
        }
    });

    // ── POST /api/sync ────────────────────────────────────────────────────────
    app.MapPost("/api/sync", async (
        HttpRequest req,
        ExcelReaderService excelReader,
        SyncOrchestrator orchestrator,
        ResponseSetService rsService,
        IOptions<SyncOptions> syncOpts,
        ILogger<Program> logger) =>
    {
        var form = await req.ReadFormAsync();

        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "Keine Datei übergeben." });

        var gesellschaft = form["gesellschaft"].ToString().Trim();
        if (string.IsNullOrEmpty(gesellschaft))
            return Results.BadRequest(new { error = "Gesellschaft ist erforderlich." });

        var responseSetName = form["responseSet"].ToString().Trim();
        if (string.IsNullOrEmpty(responseSetName))
            responseSetName = syncOpts.Value.ResponseSetName;
        var dryRun          = form["dryRun"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!int.TryParse(form["sheetIndex"],         out var sheetIndex))         sheetIndex         = 0;
        if (!int.TryParse(form["startRow"],           out var startRow))           startRow           = 1;
        if (!int.TryParse(form["filialNrCol"],        out var filialNrCol))        filialNrCol        = 0;
        if (!int.TryParse(form["strasseCol"],         out var strasseCol))         strasseCol         = 1;
        if (!int.TryParse(form["plzCol"],             out var plzCol))             plzCol             = 2;
        if (!int.TryParse(form["ortCol"],             out var ortCol))             ortCol             = 3;
        if (!int.TryParse(form["nameErweiterungCol"], out var nameErweiterungCol)) nameErweiterungCol = 4;

        var options = new ExcelReadOptions(
            SheetIndex:           sheetIndex,
            StartRow:             startRow,
            FilialNrCol:          filialNrCol,
            StrasseCol:           strasseCol,
            PlzCol:               plzCol,
            OrtCol:               ortCol,
            NamensErweiterungCol: nameErweiterungCol);

        // Temporäre Datei anlegen
        var tmpPath = Path.Combine(Path.GetTempPath(), $"sc-sync-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var fs = File.Create(tmpPath))
                await file.CopyToAsync(fs);

            var locations = excelReader.Read(tmpPath, options);

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            var syncReport = await orchestrator.RunAsync(
                locations, gesellschaft, dryRunOverride: dryRun, ct: cts.Token);

            ResponseSetSyncReport? rsReport = null;
            if (!string.IsNullOrEmpty(responseSetName))
            {
                rsReport = await rsService.RunAsync(
                    locations, responseSetName, dryRunOverride: dryRun, ct: cts.Token);
            }

            return Results.Ok(new
            {
                dryRun,
                syncReport = new
                {
                    gesellschaftId   = syncReport.GesellschaftId,
                    gesellschaftName = syncReport.GesellschaftName,
                    created          = syncReport.Created,
                    skipped          = syncReport.Skipped,
                    failed           = syncReport.Failed,
                    nameDrifts       = syncReport.NameDrifts,
                    total            = syncReport.Total,
                },
                responseSetReport = rsReport is null ? null : new
                {
                    responseSetId   = rsReport.ResponseSetId,
                    responseSetName = rsReport.ResponseSetName,
                    created         = rsReport.Added,
                    skipped         = rsReport.Skipped + rsReport.SkippedNoExt,
                    failed          = rsReport.Failed,
                },
            });
        }
        catch (FileNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unerwarteter Fehler im Web-Sync");
            return Results.Problem(ex.Message);
        }
        finally
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
    });

    var logger2 = app.Services.GetRequiredService<ILogger<Program>>();
    logger2.LogInformation("Web-UI verfügbar unter: http://localhost:5000");

    await app.RunAsync("http://localhost:5000");
    return 0;
}
