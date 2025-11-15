namespace DeltaList.Shared.Models;

public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public string Status { get; set; } = "active"; // active, suspended, revoked
    public Dictionary<string, string> Metadata { get; set; } = new();
    public int ShardId { get; set; }
}

public class ConnectionInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public long MessagesSent { get; set; }
    public long MessagesReceived { get; set; }
    public DateTime LastHeartbeat { get; set; }
}

public class BlacklistState
{
    public ulong CurrentSeqNo { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public HashSet<string> PanTokens { get; set; } = new();
    public int ShardId { get; set; }
}

public class EventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DeviceId { get; set; } = string.Empty;
    public long DeviceTimestampUtc { get; set; }
    public long ReceivedTimestampUtc { get; set; }
    public uint BatchSeq { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; set; } = new();
    public string TransactionId { get; set; } = string.Empty;
}
