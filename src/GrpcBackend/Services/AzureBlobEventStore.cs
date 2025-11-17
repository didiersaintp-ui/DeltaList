using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DeltaList.Shared.Messages;
using DeltaList.Shared.Models;
using DeltaList.Shared.Configuration;
using System.Text;
using System.Text.Json;
using System.IO.Compression;

namespace GrpcBackend.Services;

/// <summary>
/// Azure Blob Storage implementation of event store
/// Stores events as JSON in Azure Blob Storage with optional compression
/// Falls back to in-memory store if Azure is not configured
/// </summary>
public class AzureBlobEventStore : IEventStore
{
    private readonly ILogger<AzureBlobEventStore> _logger;
    private readonly BlobContainerClient? _containerClient;
    private readonly bool _enableCompression;
    private readonly InMemoryEventStore _fallbackStore;
    private readonly bool _isAzureConfigured;

    public AzureBlobEventStore(
        ILogger<AzureBlobEventStore> logger,
        BackendSettings settings,
        ILogger<InMemoryEventStore> fallbackLogger)
    {
        _logger = logger;
        _enableCompression = settings.Storage.EnableCompression;

        // Try to initialize Azure Blob Storage
        if (!string.IsNullOrEmpty(settings.Storage.ConnectionString))
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(settings.Storage.ConnectionString);
                _containerClient = blobServiceClient.GetBlobContainerClient(settings.Storage.EventsContainerName);
                _containerClient.CreateIfNotExists();

                _isAzureConfigured = true;
                _logger.LogInformation(
                    "Azure Blob Storage initialized for events (container: {Container}, compression: {Compression})",
                    settings.Storage.EventsContainerName,
                    _enableCompression);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to initialize Azure Blob Storage, falling back to in-memory store");
                _isAzureConfigured = false;
            }
        }
        else
        {
            _logger.LogInformation("Azure Blob Storage not configured, using in-memory store");
            _isAzureConfigured = false;
        }

        // Always create fallback store
        _fallbackStore = new InMemoryEventStore(fallbackLogger);
    }

    public async Task StoreBatchAsync(Batch batch, DateTime receivedAt)
    {
        if (!_isAzureConfigured || _containerClient == null)
        {
            // Use fallback in-memory store
            await _fallbackStore.StoreBatchAsync(batch, receivedAt);
            return;
        }

        try
        {
            var receivedTicks = new DateTimeOffset(receivedAt).ToUnixTimeMilliseconds();

            // Create records for each event
            var records = batch.Events.Select(evt => new EventRecord
            {
                Id = Guid.NewGuid().ToString(),
                DeviceId = evt.DeviceId,
                DeviceTimestampUtc = evt.DeviceTimestampUtc,
                ReceivedTimestampUtc = receivedTicks,
                BatchSeq = batch.BatchSeq,
                EventType = evt.EventType,
                TransactionId = evt.TransactionId,
                Attributes = evt.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value)
            }).ToList();

            // Organize by device and date for efficient querying
            var blobName = GenerateBlobName(batch.DeviceId, receivedAt, batch.BatchSeq);

            // Serialize to JSON
            var json = JsonSerializer.Serialize(new
            {
                batch.DeviceId,
                batch.BatchSeq,
                ReceivedAt = receivedAt,
                EventCount = batch.Events.Count,
                Events = records
            }, new JsonSerializerOptions { WriteIndented = false });

            // Upload to blob
            var content = Encoding.UTF8.GetBytes(json);

            if (_enableCompression)
            {
                content = await CompressAsync(content);
            }

            var blobClient = _containerClient.GetBlobClient(blobName);

            var metadata = new Dictionary<string, string>
            {
                { "device_id", batch.DeviceId },
                { "batch_seq", batch.BatchSeq.ToString() },
                { "event_count", batch.Events.Count.ToString() },
                { "received_at", receivedAt.ToString("o") },
                { "compressed", _enableCompression.ToString() }
            };

            await blobClient.UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions
                {
                    Metadata = metadata,
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = _enableCompression ? "application/gzip" : "application/json"
                    }
                });

            _logger.LogDebug(
                "Stored batch {BatchSeq} from device {DeviceId} to Azure Blob: {BlobName} ({Size} bytes)",
                batch.BatchSeq,
                batch.DeviceId,
                blobName,
                content.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error storing batch {BatchSeq} from device {DeviceId} to Azure, falling back to in-memory",
                batch.BatchSeq,
                batch.DeviceId);

            // Fallback to in-memory store on error
            await _fallbackStore.StoreBatchAsync(batch, receivedAt);
        }
    }

    public async Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, DateTime from, DateTime to)
    {
        if (!_isAzureConfigured || _containerClient == null)
        {
            return await _fallbackStore.GetEventsAsync(deviceId, from, to);
        }

        try
        {
            var results = new List<EventRecord>();

            // List blobs for this device and date range
            var prefix = $"events/{deviceId}/";
            await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix))
            {
                // Parse date from blob name to filter by date range
                if (TryParseDateFromBlobName(blobItem.Name, out var blobDate))
                {
                    if (blobDate.Date >= from.Date && blobDate.Date <= to.Date)
                    {
                        var events = await ReadEventsFromBlobAsync(blobItem.Name);
                        results.AddRange(events);
                    }
                }
            }

            var fromTicks = new DateTimeOffset(from).ToUnixTimeMilliseconds();
            var toTicks = new DateTimeOffset(to).ToUnixTimeMilliseconds();

            // Filter by exact timestamp range
            return results
                .Where(e => e.ReceivedTimestampUtc >= fromTicks && e.ReceivedTimestampUtc <= toTicks)
                .OrderBy(e => e.ReceivedTimestampUtc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading events from Azure for device {DeviceId}, using fallback", deviceId);
            return await _fallbackStore.GetEventsAsync(deviceId, from, to);
        }
    }

    public async Task<long> GetEventCountAsync(string deviceId)
    {
        if (!_isAzureConfigured || _containerClient == null)
        {
            return await _fallbackStore.GetEventCountAsync(deviceId);
        }

        try
        {
            long count = 0;
            var prefix = $"events/{deviceId}/";

            await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix))
            {
                // Read metadata to get event count without downloading blob
                var blobClient = _containerClient.GetBlobClient(blobItem.Name);
                var properties = await blobClient.GetPropertiesAsync();

                if (properties.Value.Metadata.TryGetValue("event_count", out var eventCountStr))
                {
                    if (int.TryParse(eventCountStr, out var eventCount))
                    {
                        count += eventCount;
                    }
                }
            }

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting event count from Azure for device {DeviceId}, using fallback", deviceId);
            return await _fallbackStore.GetEventCountAsync(deviceId);
        }
    }

    // Helper methods

    private static string GenerateBlobName(string deviceId, DateTime timestamp, uint batchSeq)
    {
        // Structure: events/{deviceId}/{year}/{month}/{day}/{timestamp}_{batchSeq}.json
        return $"events/{deviceId}/{timestamp:yyyy}/{timestamp:MM}/{timestamp:dd}/{timestamp:yyyyMMddHHmmss}_{batchSeq}.json";
    }

    private static bool TryParseDateFromBlobName(string blobName, out DateTime date)
    {
        // Example: events/device123/2025/11/17/20251117120000_1.json
        try
        {
            var parts = blobName.Split('/');
            if (parts.Length >= 5)
            {
                var year = int.Parse(parts[2]);
                var month = int.Parse(parts[3]);
                var day = int.Parse(parts[4]);
                date = new DateTime(year, month, day);
                return true;
            }
        }
        catch
        {
            // Ignore parse errors
        }

        date = DateTime.MinValue;
        return false;
    }

    private async Task<List<EventRecord>> ReadEventsFromBlobAsync(string blobName)
    {
        if (_containerClient == null)
            return new List<EventRecord>();

        var blobClient = _containerClient.GetBlobClient(blobName);
        var download = await blobClient.DownloadContentAsync();

        var content = download.Value.Content.ToArray();

        // Check if compressed
        var properties = await blobClient.GetPropertiesAsync();
        var isCompressed = properties.Value.Metadata.TryGetValue("compressed", out var compressedStr)
            && bool.Parse(compressedStr);

        if (isCompressed)
        {
            content = await DecompressAsync(content);
        }

        var json = Encoding.UTF8.GetString(content);
        var data = JsonSerializer.Deserialize<BatchData>(json);

        return data?.Events ?? new List<EventRecord>();
    }

    private static async Task<byte[]> CompressAsync(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            await gzip.WriteAsync(data);
        }
        return output.ToArray();
    }

    private static async Task<byte[]> DecompressAsync(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        {
            await gzip.CopyToAsync(output);
        }
        return output.ToArray();
    }

    // Helper class for deserialization
    private class BatchData
    {
        public string DeviceId { get; set; } = string.Empty;
        public uint BatchSeq { get; set; }
        public DateTime ReceivedAt { get; set; }
        public int EventCount { get; set; }
        public List<EventRecord> Events { get; set; } = new();
    }
}
