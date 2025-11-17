using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace GrpcDeviceSimulator.Metrics;

/// <summary>
/// Advanced metrics collector with percentile calculations and CSV export
/// </summary>
public class AdvancedMetricsCollector
{
    private readonly ConcurrentBag<double> _latencies = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly string _outputDirectory;

    public AdvancedMetricsCollector(string outputDirectory = "metrics")
    {
        _outputDirectory = outputDirectory;
        Directory.CreateDirectory(_outputDirectory);
    }

    /// <summary>
    /// Record a latency measurement in milliseconds
    /// </summary>
    public void RecordLatency(double latencyMs)
    {
        _latencies.Add(latencyMs);
    }

    /// <summary>
    /// Increment a counter
    /// </summary>
    public void IncrementCounter(string name, long value = 1)
    {
        _counters.AddOrUpdate(name, value, (_, current) => current + value);
    }

    /// <summary>
    /// Get counter value
    /// </summary>
    public long GetCounter(string name)
    {
        return _counters.TryGetValue(name, out var value) ? value : 0;
    }

    /// <summary>
    /// Calculate latency percentiles
    /// </summary>
    public LatencyStats CalculateLatencyStats()
    {
        var latencyArray = _latencies.ToArray();
        if (latencyArray.Length == 0)
        {
            return new LatencyStats();
        }

        Array.Sort(latencyArray);

        return new LatencyStats
        {
            Count = latencyArray.Length,
            Min = latencyArray[0],
            Max = latencyArray[^1],
            Mean = latencyArray.Average(),
            P50 = GetPercentile(latencyArray, 0.50),
            P95 = GetPercentile(latencyArray, 0.95),
            P99 = GetPercentile(latencyArray, 0.99),
            P999 = GetPercentile(latencyArray, 0.999)
        };
    }

    /// <summary>
    /// Get success rate
    /// </summary>
    public double GetSuccessRate()
    {
        var total = GetCounter("requests_total");
        var failed = GetCounter("requests_failed");
        if (total == 0) return 100.0;
        return ((total - failed) / (double)total) * 100.0;
    }

    /// <summary>
    /// Export metrics to CSV file
    /// </summary>
    public async Task ExportToCsvAsync(string filename)
    {
        var stats = CalculateLatencyStats();
        var filepath = Path.Combine(_outputDirectory, filename);

        var csv = new StringBuilder();
        csv.AppendLine("Metric,Value");
        csv.AppendLine($"Timestamp,{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine($"Uptime (seconds),{_uptime.Elapsed.TotalSeconds:F2}");
        csv.AppendLine();

        // Latency metrics
        csv.AppendLine("# Latency Statistics");
        csv.AppendLine($"Latency Count,{stats.Count}");
        csv.AppendLine($"Latency Min (ms),{stats.Min:F2}");
        csv.AppendLine($"Latency Max (ms),{stats.Max:F2}");
        csv.AppendLine($"Latency Mean (ms),{stats.Mean:F2}");
        csv.AppendLine($"Latency P50 (ms),{stats.P50:F2}");
        csv.AppendLine($"Latency P95 (ms),{stats.P95:F2}");
        csv.AppendLine($"Latency P99 (ms),{stats.P99:F2}");
        csv.AppendLine($"Latency P99.9 (ms),{stats.P999:F2}");
        csv.AppendLine();

        // Counter metrics
        csv.AppendLine("# Counters");
        foreach (var kvp in _counters.OrderBy(k => k.Key))
        {
            csv.AppendLine($"{kvp.Key},{kvp.Value}");
        }
        csv.AppendLine();

        // Derived metrics
        csv.AppendLine("# Derived Metrics");
        csv.AppendLine($"Success Rate (%),{GetSuccessRate():F2}");
        var throughput = GetCounter("batches_sent") / _uptime.Elapsed.TotalSeconds;
        csv.AppendLine($"Throughput (batches/sec),{throughput:F2}");
        var eventThroughput = GetCounter("events_sent") / _uptime.Elapsed.TotalSeconds;
        csv.AppendLine($"Event Throughput (events/sec),{eventThroughput:F2}");

        await File.WriteAllTextAsync(filepath, csv.ToString());
        Log.Information("Metrics exported to {Filepath}", filepath);
    }

    /// <summary>
    /// Export raw latency data for further analysis
    /// </summary>
    public async Task ExportRawLatenciesAsync(string filename)
    {
        var filepath = Path.Combine(_outputDirectory, filename);
        var latencies = _latencies.ToArray();

        var csv = new StringBuilder();
        csv.AppendLine("Latency_ms");
        foreach (var latency in latencies)
        {
            csv.AppendLine($"{latency:F3}");
        }

        await File.WriteAllTextAsync(filepath, csv.ToString());
        Log.Information("Raw latencies exported to {Filepath}", filepath);
    }

    /// <summary>
    /// Print summary to console
    /// </summary>
    public void PrintSummary()
    {
        var stats = CalculateLatencyStats();

        Log.Information("========== Metrics Summary ==========");
        Log.Information("Uptime: {Uptime:F2}s", _uptime.Elapsed.TotalSeconds);
        Log.Information("");
        Log.Information("Latency Statistics (ms):");
        Log.Information("  Count: {Count}", stats.Count);
        Log.Information("  Min: {Min:F2}", stats.Min);
        Log.Information("  Max: {Max:F2}", stats.Max);
        Log.Information("  Mean: {Mean:F2}", stats.Mean);
        Log.Information("  P50: {P50:F2}", stats.P50);
        Log.Information("  P95: {P95:F2}", stats.P95);
        Log.Information("  P99: {P99:F2}", stats.P99);
        Log.Information("  P99.9: {P999:F2}", stats.P999);
        Log.Information("");
        Log.Information("Counters:");
        foreach (var kvp in _counters.OrderBy(k => k.Key))
        {
            Log.Information("  {Name}: {Value}", kvp.Key, kvp.Value);
        }
        Log.Information("");
        Log.Information("Success Rate: {Rate:F2}%", GetSuccessRate());
        Log.Information("=====================================");
    }

    private double GetPercentile(double[] sortedArray, double percentile)
    {
        if (sortedArray.Length == 0) return 0;

        var index = (int)Math.Ceiling(percentile * sortedArray.Length) - 1;
        index = Math.Max(0, Math.Min(sortedArray.Length - 1, index));
        return sortedArray[index];
    }
}

public class LatencyStats
{
    public int Count { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Mean { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
    public double P999 { get; set; }
}
