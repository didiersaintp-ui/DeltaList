using CommandLine;

namespace MqttDeviceSimulator;

public class SimulatorOptions
{
    [Option('s', "server", Required = false, Default = "localhost", HelpText = "MQTT broker hostname")]
    public string BrokerHost { get; set; } = "localhost";

    [Option('p', "port", Required = false, Default = 1883, HelpText = "MQTT broker port")]
    public int BrokerPort { get; set; } = 1883;

    [Option('n', "devices", Required = false, Default = 10, HelpText = "Number of devices to simulate")]
    public int DeviceCount { get; set; } = 10;

    [Option('d', "duration", Required = false, Default = 300, HelpText = "Simulation duration in seconds (0 = infinite)")]
    public int DurationSeconds { get; set; } = 300;

    [Option('b', "batch-interval", Required = false, Default = 60, HelpText = "Batch send interval in seconds")]
    public int BatchIntervalSeconds { get; set; } = 60;

    [Option('e', "events-per-batch", Required = false, Default = 50, HelpText = "Number of events per batch")]
    public int EventsPerBatch { get; set; } = 50;

    [Option("latency-ms", Required = false, Default = 0, HelpText = "Simulate 4G latency (random 0-N ms)")]
    public int MaxLatencyMs { get; set; } = 0;

    [Option("packet-loss", Required = false, Default = 0.0, HelpText = "Simulate packet loss percentage (0.0-1.0)")]
    public double PacketLossRate { get; set; } = 0.0;

    [Option("reconnect-rate", Required = false, Default = 0.0, HelpText = "Random reconnection rate (0.0-1.0)")]
    public double ReconnectRate { get; set; } = 0.0;

    [Option("prefix", Required = false, Default = "device", HelpText = "Device ID prefix")]
    public string DeviceIdPrefix { get; set; } = "device";

    [Option("use-tls", Required = false, Default = false, HelpText = "Use TLS connection")]
    public bool UseTls { get; set; } = false;

    [Option("username", Required = false, Default = "", HelpText = "MQTT username")]
    public string Username { get; set; } = "";

    [Option("password", Required = false, Default = "", HelpText = "MQTT password")]
    public string Password { get; set; } = "";

    [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose logging")]
    public bool Verbose { get; set; } = false;

    // Authentication options
    [Option("auth-server", Required = false, Default = "", HelpText = "Authentication server URL (empty = no auth)")]
    public string AuthServerUrl { get; set; } = "";

    [Option("use-auth", Required = false, Default = false, HelpText = "Enable JWT authentication")]
    public bool UseAuthentication { get; set; } = false;

    // QoS options
    [Option("qos", Required = false, Default = 1, HelpText = "MQTT QoS level (0, 1, or 2)")]
    public int QosLevel { get; set; } = 1;

    // Test modes
    [Option("test-mode", Required = false, Default = "normal", HelpText = "Test mode: normal, burst, staggered, stress")]
    public string TestMode { get; set; } = "normal";

    [Option("stagger-delay-ms", Required = false, Default = 100, HelpText = "Delay between device starts in staggered mode (ms)")]
    public int StaggerDelayMs { get; set; } = 100;

    // Metrics options
    [Option("export-metrics", Required = false, Default = false, HelpText = "Export metrics to CSV")]
    public bool ExportMetrics { get; set; } = false;

    [Option("metrics-dir", Required = false, Default = "metrics", HelpText = "Directory for metrics export")]
    public string MetricsDirectory { get; set; } = "metrics";

    // Reconnection options
    [Option("max-retry", Required = false, Default = 5, HelpText = "Maximum reconnection retries")]
    public int MaxReconnectRetries { get; set; } = 5;

    [Option("exponential-backoff", Required = false, Default = true, HelpText = "Use exponential backoff for reconnections")]
    public bool UseExponentialBackoff { get; set; } = true;
}
