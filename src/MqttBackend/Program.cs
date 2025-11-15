using MqttBackend.Services;
using DeltaList.Shared.Configuration;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using GrpcBackend.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Load configuration
var backendSettings = new BackendSettings();
builder.Configuration.Bind("Backend", backendSettings);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add distributed cache (Redis)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = backendSettings.Redis.ConnectionString;
    options.InstanceName = backendSettings.Redis.InstanceName;
});

// Add singleton services
builder.Services.AddSingleton(backendSettings);
builder.Services.AddSingleton(new PanTokenizer(
    backendSettings.Security.SecretKey,
    backendSettings.Security.Salt));
builder.Services.AddSingleton(new MetricsCollector("MqttBackend"));
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<IBlacklistManager, BlacklistManager>();

// Add MQTT service
builder.Services.AddSingleton<MqttBrokerClient>();
builder.Services.AddHostedService<MqttBrokerClient>(sp => sp.GetRequiredService<MqttBrokerClient>());

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("MqttBackend"))
            .AddMeter("MqttBackend")
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();
    });

var app = builder.Build();

// Initialize blacklist manager
var blacklistManager = app.Services.GetRequiredService<IBlacklistManager>();
if (blacklistManager is BlacklistManager bm)
{
    await bm.InitializeAsync();
}

// Configure middleware
app.UseSerilogRequestLogging();
app.UseRouting();

// Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();

// Health check endpoint
app.MapGet("/health", (MqttBrokerClient mqttClient) => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    mqttConnected = mqttClient.IsConnected
});

// Info endpoint
app.MapGet("/info", (IEventStore eventStore, MqttBrokerClient mqttClient) =>
{
    var inMemoryStore = eventStore as InMemoryEventStore;
    return new
    {
        serviceName = "DeltaList MQTT Backend (PoC B)",
        version = "1.0.0",
        environment = backendSettings.Environment,
        mqttBroker = backendSettings.Mqtt.BrokerHost,
        mqttConnected = mqttClient.IsConnected,
        totalEventsStored = inMemoryStore?.GetTotalEventCount() ?? 0,
        uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
    };
});

// Admin API endpoints
app.MapPost("/api/blacklist/add", async (
    BlacklistAddRequest request,
    IBlacklistManager blacklistManager,
    MqttBrokerClient mqttClient,
    MetricsCollector metrics,
    ILogger<Program> logger) =>
{
    try
    {
        var delta = await blacklistManager.AddToBlacklistAsync(
            request.PanTokens,
            request.Reason ?? "Manual add",
            request.UpdatedBy ?? "admin");

        // Publish delta to MQTT topic
        await mqttClient.PublishBlacklistDeltaAsync(delta);

        return Results.Ok(new
        {
            success = true,
            message = $"Added {delta.Added.Count} tokens to blacklist",
            deltaSeqNo = delta.SeqNo
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error adding to blacklist");
        metrics.RecordError("blacklist_add_error");
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/blacklist/remove", async (
    BlacklistRemoveRequest request,
    IBlacklistManager blacklistManager,
    MqttBrokerClient mqttClient,
    MetricsCollector metrics,
    ILogger<Program> logger) =>
{
    try
    {
        var delta = await blacklistManager.RemoveFromBlacklistAsync(
            request.PanTokens,
            request.Reason ?? "Manual remove",
            request.UpdatedBy ?? "admin");

        // Publish delta to MQTT topic
        await mqttClient.PublishBlacklistDeltaAsync(delta);

        return Results.Ok(new
        {
            success = true,
            message = $"Removed {delta.Removed.Count} tokens from blacklist",
            deltaSeqNo = delta.SeqNo
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error removing from blacklist");
        metrics.RecordError("blacklist_remove_error");
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/blacklist", async (IBlacklistManager blacklistManager) =>
{
    var state = await blacklistManager.GetCurrentBlacklistAsync("global");
    return Results.Ok(new
    {
        seqNo = state?.CurrentSeqNo ?? 0,
        count = state?.PanTokens.Count ?? 0,
        tokens = state?.PanTokens ?? new HashSet<string>()
    });
});

Log.Information("Starting DeltaList MQTT Backend");
Log.Information("MQTT Broker: {Broker}:{Port}", backendSettings.Mqtt.BrokerHost, backendSettings.Mqtt.BrokerPort);
Log.Information("Metrics endpoint available on port {MetricsPort}", backendSettings.Monitoring.MetricsPort);

app.Run();

// Request/Response models
public record BlacklistAddRequest(List<string> PanTokens, string? Reason, string? UpdatedBy);
public record BlacklistRemoveRequest(List<string> PanTokens, string? Reason, string? UpdatedBy);
