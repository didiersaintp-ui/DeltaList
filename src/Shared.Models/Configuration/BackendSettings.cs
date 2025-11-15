namespace DeltaList.Shared.Configuration;

public class BackendSettings
{
    public string Environment { get; set; } = "Development";

    // Security
    public SecuritySettings Security { get; set; } = new();

    // Storage
    public StorageSettings Storage { get; set; } = new();

    // Redis
    public RedisSettings Redis { get; set; } = new();

    // Metrics & Monitoring
    public MonitoringSettings Monitoring { get; set; } = new();

    // gRPC specific
    public GrpcSettings Grpc { get; set; } = new();

    // MQTT specific
    public MqttSettings Mqtt { get; set; } = new();
}

public class SecuritySettings
{
    public string SecretKey { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public string JwtIssuer { get; set; } = "DeltaList";
    public string JwtAudience { get; set; } = "DeltaListDevices";
    public int JwtExpirationMinutes { get; set; } = 60;
}

public class StorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string EventsContainerName { get; set; } = "events";
    public string BlacklistContainerName { get; set; } = "blacklist";
    public bool EnableCompression { get; set; } = true;
}

public class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "DeltaList:";
    public int DatabaseId { get; set; } = 0;
}

public class MonitoringSettings
{
    public bool EnablePrometheus { get; set; } = true;
    public bool EnableOpenTelemetry { get; set; } = true;
    public string OtlpEndpoint { get; set; } = string.Empty;
    public int MetricsPort { get; set; } = 9090;
}

public class GrpcSettings
{
    public int Port { get; set; } = 5001;
    public int MaxConcurrentStreams { get; set; } = 100000;
    public int KeepAliveIntervalSeconds { get; set; } = 30;
    public int KeepAliveTimeoutSeconds { get; set; } = 10;
    public bool EnableReflection { get; set; } = true;
}

public class MqttSettings
{
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string ClientId { get; set; } = "DeltaListBackend";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; } = true;
    public int KeepAlivePeriodSeconds { get; set; } = 60;
    public int MaxPendingMessages { get; set; } = 10000;
}
