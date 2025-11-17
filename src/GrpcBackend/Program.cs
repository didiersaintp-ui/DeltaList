using GrpcBackend.Services;
using DeltaList.Shared.Configuration;
using DeltaList.Shared.Security;
using DeltaList.Shared.Metrics;
using Microsoft.AspNetCore.Server.Kestrel.Core;
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

// Configure Kestrel for gRPC
builder.WebHost.ConfigureKestrel(options =>
{
    // HTTP/2 endpoint for gRPC
    options.ListenAnyIP(backendSettings.Grpc.Port, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });

    // HTTP/1.1 endpoint for metrics (Prometheus)
    options.ListenAnyIP(backendSettings.Monitoring.MetricsPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    options.Limits.MaxConcurrentConnections = backendSettings.Grpc.MaxConcurrentStreams;
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(backendSettings.Grpc.KeepAliveTimeoutSeconds);
});

// Add services
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16MB
    options.MaxSendMessageSize = 16 * 1024 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddGrpcReflection();

// Add distributed cache (Redis)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = backendSettings.Redis.ConnectionString;
    options.InstanceName = backendSettings.Redis.InstanceName;
});

// Add singleton services
builder.Services.AddSingleton(backendSettings);

// Security services
builder.Services.AddSingleton(new PanTokenizer(
    backendSettings.Security.SecretKey,
    backendSettings.Security.Salt));
builder.Services.AddSingleton<DeltaList.Shared.Security.IJwtTokenService>(sp =>
    new DeltaList.Shared.Security.JwtTokenService(
        backendSettings.Security.SecretKey,
        backendSettings.Security.JwtIssuer,
        backendSettings.Security.JwtAudience));

// Device registry
builder.Services.AddSingleton<DeltaList.Shared.Interfaces.IDeviceRegistry, DeltaList.Shared.Interfaces.InMemoryDeviceRegistry>();

// Rate limiting
builder.Services.AddSingleton<IRateLimiter>(sp =>
    new TokenBucketRateLimiter(
        maxTokens: 60, // Allow 60 batches per minute
        refillRatePerMinute: 60));

// Metrics
builder.Services.AddSingleton(new MetricsCollector("GrpcBackend"));

// Storage - Event Store (InMemory for PoC)
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();

// Blacklist Manager (with Azure persistence)
builder.Services.AddSingleton<IBlacklistManager, BlacklistManager>();

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("GrpcBackend"))
            .AddMeter("GrpcBackend")
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

// Map gRPC services
app.MapGrpcService<DeviceAuthServiceImpl>();
app.MapGrpcService<DeviceStreamServiceImpl>();
app.MapGrpcService<BlacklistAdminServiceImpl>();

if (backendSettings.Grpc.EnableReflection)
{
    app.MapGrpcReflectionService();
}

// Prometheus metrics endpoint (on different port)
app.MapPrometheusScrapingEndpoint();

// Health check endpoint
app.MapGet("/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    activeConnections = DeviceStreamServiceImpl.GetActiveConnectionCount()
});

// Info endpoint
app.MapGet("/info", (IEventStore eventStore) =>
{
    var inMemoryStore = eventStore as InMemoryEventStore;
    return new
    {
        serviceName = "DeltaList gRPC Backend (PoC A)",
        version = "1.0.0",
        environment = backendSettings.Environment,
        activeConnections = DeviceStreamServiceImpl.GetActiveConnectionCount(),
        totalEventsStored = inMemoryStore?.GetTotalEventCount() ?? 0,
        uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()
    };
});

Log.Information("Starting DeltaList gRPC Backend on port {Port}", backendSettings.Grpc.Port);
Log.Information("Metrics endpoint available on port {MetricsPort}", backendSettings.Monitoring.MetricsPort);

app.Run();
