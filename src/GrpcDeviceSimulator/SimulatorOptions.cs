using CommandLine;

namespace GrpcDeviceSimulator;

public class SimulatorOptions
{
    [Option('s', "server", Required = false, Default = "localhost:5001", HelpText = "gRPC server address")]
    public string ServerAddress { get; set; } = "localhost:5001";

    [Option('n', "devices", Required = false, Default = 10, HelpText = "Number of devices to simulate")]
    public int DeviceCount { get; set; } = 10;

    [Option('d', "duration", Required = false, Default = 300, HelpText = "Simulation duration in seconds (0 = infinite)")]
    public int DurationSeconds { get; set; } = 300;

    [Option('b', "batch-interval", Required = false, Default = 60, HelpText = "Batch send interval in seconds")]
    public int BatchIntervalSeconds { get; set; } = 60;

    [Option('e', "events-per-batch", Required = false, Default = 50, HelpText = "Number of events per batch")]
    public int EventsPerBatch { get; set; } = 50;

    [Option('h', "heartbeat-interval", Required = false, Default = 30, HelpText = "Heartbeat interval in seconds")]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    [Option("latency-ms", Required = false, Default = 0, HelpText = "Simulate 4G latency (random 0-N ms)")]
    public int MaxLatencyMs { get; set; } = 0;

    [Option("packet-loss", Required = false, Default = 0.0, HelpText = "Simulate packet loss percentage (0.0-1.0)")]
    public double PacketLossRate { get; set; } = 0.0;

    [Option("reconnect-rate", Required = false, Default = 0.0, HelpText = "Random reconnection rate (0.0-1.0)")]
    public double ReconnectRate { get; set; } = 0.0;

    [Option('p', "prefix", Required = false, Default = "device", HelpText = "Device ID prefix")]
    public string DeviceIdPrefix { get; set; } = "device";

    [Option("use-tls", Required = false, Default = false, HelpText = "Use TLS connection")]
    public bool UseTls { get; set; } = false;

    [Option('v', "verbose", Required = false, Default = false, HelpText = "Verbose logging")]
    public bool Verbose { get; set; } = false;
}
