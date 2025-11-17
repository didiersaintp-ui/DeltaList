using MqttBackend.Services;
using DeltaList.Shared.Configuration;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using DeltaList.Shared.Interfaces;
using DeltaList.Shared.Services;
using DeltaList.Shared.Messages;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using System.Diagnostics;

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
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DeltaList MQTT Backend API",
        Version = "v1",
        Description = "Admin REST API for DeltaList MQTT Backend (PoC B) - Manage devices, blacklist, and monitor system metrics",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "DeltaList Team"
        }
    });
    options.TagActionsBy(api => new[] { api.GroupName ?? api.ActionDescriptor.DisplayName ?? "Default" });
    options.DocInclusionPredicate((name, api) => true);
});

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

// Add MqttAuthenticationService as singleton
builder.Services.AddSingleton<MqttBackend.Services.MqttAuthenticationService>();

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

// Enable Swagger in all environments (PoC)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DeltaList MQTT Backend API v1");
    options.RoutePrefix = "swagger"; // Access at /swagger
});

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
        var (seqNo, devicesNotified) = await blacklistManager.AddToBlacklistAsync(
            request.PanTokens,
            request.ShardId ?? 0,
            request.Reason ?? "Manual add",
            request.UpdatedBy ?? "admin");

        // Get the delta to publish
        var deltas = await blacklistManager.GetDeltasSinceAsync(seqNo - 1, request.ShardId ?? 0);
        var delta = deltas.FirstOrDefault();

        if (delta != null)
        {
            // Publish delta to MQTT topic
            await mqttClient.PublishBlacklistDeltaAsync(delta);
        }

        return Results.Ok(new
        {
            success = true,
            message = $"Added tokens to blacklist",
            deltaSeqNo = seqNo,
            devicesNotified = devicesNotified
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
        var (seqNo, devicesNotified) = await blacklistManager.RemoveFromBlacklistAsync(
            request.PanTokens,
            request.ShardId ?? 0,
            request.Reason ?? "Manual remove",
            request.UpdatedBy ?? "admin");

        // Get the delta to publish
        var deltas = await blacklistManager.GetDeltasSinceAsync(seqNo - 1, request.ShardId ?? 0);
        var delta = deltas.FirstOrDefault();

        if (delta != null)
        {
            // Publish delta to MQTT topic
            await mqttClient.PublishBlacklistDeltaAsync(delta);
        }

        return Results.Ok(new
        {
            success = true,
            message = $"Removed tokens from blacklist",
            deltaSeqNo = seqNo,
            devicesNotified = devicesNotified
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error removing from blacklist");
        metrics.RecordError("blacklist_remove_error");
        return Results.Problem(ex.Message);
    }
});

app.MapGet("/api/blacklist", async (IBlacklistManager blacklistManager, int shardId = 0) =>
{
    var state = await blacklistManager.GetBlacklistStateAsync(shardId);
    return Results.Ok(new
    {
        seqNo = state.CurrentSeqNo,
        count = state.PanTokens.Count,
        shardId = state.ShardId,
        tokens = state.PanTokens
    });
});

// Additional Admin API endpoints
app.MapGet("/api/devices", (MqttBrokerClient mqttClient) =>
{
    // For PoC, return mock connected devices
    // In production, track connections in Redis/cache
    return Results.Ok(new
    {
        connectedDevices = mqttClient.IsConnected ? 1 : 0,
        totalDevices = 1,
        devices = new[]
        {
            new
            {
                deviceId = "mock-device-001",
                status = mqttClient.IsConnected ? "connected" : "disconnected",
                lastSeen = DateTime.UtcNow,
                messagesSent = 0,
                messagesReceived = 0
            }
        }
    });
})
.WithName("GetDevices")
.WithTags("Devices");

app.MapPost("/api/devices/register", async (
    DeviceRegistrationRequest request,
    MqttBackend.Services.MqttAuthenticationService authService,
    ILogger<Program> logger) =>
{
    try
    {
        var deviceId = await authService.RegisterDeviceAsync(request.DeviceId, request.Password);

        return Results.Ok(new
        {
            success = true,
            deviceId = deviceId,
            message = $"Device {deviceId} registered successfully"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error registering device {DeviceId}", request.DeviceId);
        return Results.Problem(ex.Message);
    }
})
.WithName("RegisterDevice")
.WithTags("Devices");

app.MapDelete("/api/devices/{deviceId}", async (
    string deviceId,
    MqttBackend.Services.MqttAuthenticationService authService,
    ILogger<Program> logger) =>
{
    try
    {
        await authService.RevokeDeviceAsync(deviceId);

        return Results.Ok(new
        {
            success = true,
            message = $"Device {deviceId} revoked successfully"
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error revoking device {DeviceId}", deviceId);
        return Results.Problem(ex.Message);
    }
})
.WithName("RevokeDevice")
.WithTags("Devices");

app.MapGet("/api/metrics", (MetricsCollector metrics, MqttBrokerClient mqttClient, IEventStore eventStore) =>
{
    var inMemoryStore = eventStore as InMemoryEventStore;
    return Results.Ok(new
    {
        timestamp = DateTime.UtcNow,
        mqtt = new
        {
            connected = mqttClient.IsConnected,
            broker = backendSettings.Mqtt.BrokerHost,
            qos = backendSettings.Mqtt.QoS
        },
        events = new
        {
            totalStored = inMemoryStore?.GetTotalEventCount() ?? 0
        },
        metrics = new
        {
            // Add custom metrics here
            uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
        }
    });
})
.WithName("GetMetrics")
.WithTags("Monitoring");

app.MapPost("/api/blacklist/sync", async (
    IBlacklistManager blacklistManager,
    MqttBrokerClient mqttClient,
    ILogger<Program> logger,
    int shardId = 0) =>
{
    try
    {
        // Get current blacklist state
        var state = await blacklistManager.GetBlacklistStateAsync(shardId);

        // Create a full sync delta (added only, no removed)
        var syncDelta = new BlacklistDelta
        {
            DeltaId = Guid.NewGuid().ToString(),
            SeqNo = state.CurrentSeqNo,
            TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ShardId = shardId
        };
        syncDelta.Added.AddRange(state.PanTokens);

        // Publish to all devices
        await mqttClient.PublishBlacklistDeltaAsync(syncDelta);

        return Results.Ok(new
        {
            success = true,
            message = $"Forced sync of blacklist shard {shardId} to all devices",
            tokenCount = state.PanTokens.Count,
            seqNo = state.CurrentSeqNo
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error forcing blacklist sync");
        return Results.Problem(ex.Message);
    }
})
.WithName("SyncBlacklist")
.WithTags("Blacklist");

// EMQX Authentication Webhook Endpoint
app.MapPost("/api/auth/mqtt", async (
    MqttAuthRequest request,
    MqttBackend.Services.MqttAuthenticationService authService,
    ILogger<Program> logger) =>
{
    try
    {
        var result = await authService.AuthenticateAsync(request.Username, request.Password);

        if (result.IsAuthenticated)
        {
            logger.LogInformation("MQTT authentication successful for {ClientId}", request.ClientId);
            return Results.Ok(new
            {
                result = "allow",
                is_superuser = false
            });
        }
        else
        {
            logger.LogWarning("MQTT authentication failed for {ClientId}: {Error}", request.ClientId, result.ErrorMessage);
            return Results.Json(new
            {
                result = "deny",
                is_superuser = false
            }, statusCode: 401);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error authenticating MQTT client {ClientId}", request.ClientId);
        return Results.Json(new
        {
            result = "deny",
            is_superuser = false
        }, statusCode: 500);
    }
})
.WithName("MqttAuthentication")
.WithTags("Authentication")
.AllowAnonymous();

Log.Information("Starting DeltaList MQTT Backend");
Log.Information("MQTT Broker: {Broker}:{Port}", backendSettings.Mqtt.BrokerHost, backendSettings.Mqtt.BrokerPort);
Log.Information("Metrics endpoint available on port {MetricsPort}", backendSettings.Monitoring.MetricsPort);

app.Run();

// Request/Response models
public record BlacklistAddRequest(List<string> PanTokens, int? ShardId, string? Reason, string? UpdatedBy);
public record BlacklistRemoveRequest(List<string> PanTokens, int? ShardId, string? Reason, string? UpdatedBy);
public record DeviceRegistrationRequest(string DeviceId, string Password);
public record MqttAuthRequest(string ClientId, string Username, string Password, string Peerhost, string Protocol);
