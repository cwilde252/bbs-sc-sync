using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using SafetyCultureSync.Infrastructure;
using SafetyCultureSync.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

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
    var syncReport = await orchestrator.RunAsync(locations, gesellschaft, cts.Token);
    reporter.Print(syncReport, syncOpts.DryRun);
    if (syncReport.Failed > 0) exitCode = 1;

    // ── 2. Response-Set-Sync (optional) ──────────────────────────────────────
    var rsName = responseSet ?? syncOpts.ResponseSetName;
    if (rsName is not null)
    {
        var rsReport = await rsService.RunAsync(locations, rsName, cts.Token);
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
