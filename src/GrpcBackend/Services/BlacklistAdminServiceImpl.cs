using DeltaList.Shared.Services;
using DeltaList.Shared.Metrics;
using Grpc.Core;

namespace GrpcBackend.Services;

public class BlacklistAdminServiceImpl : BlacklistAdminService.BlacklistAdminServiceBase
{
    private readonly ILogger<BlacklistAdminServiceImpl> _logger;
    private readonly IBlacklistManager _blacklistManager;
    private readonly MetricsCollector _metrics;

    public BlacklistAdminServiceImpl(
        ILogger<BlacklistAdminServiceImpl> logger,
        IBlacklistManager blacklistManager,
        MetricsCollector metrics)
    {
        _logger = logger;
        _blacklistManager = blacklistManager;
        _metrics = metrics;
    }

    public override async Task<BlacklistUpdateResponse> AddToBlacklist(
        BlacklistUpdateRequest request,
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation(
                "Adding {Count} tokens to blacklist. Reason: {Reason}, By: {UpdatedBy}",
                request.PanTokens.Count, request.Reason, request.UpdatedBy);

            var delta = await _blacklistManager.AddToBlacklistAsync(
                request.PanTokens,
                request.Reason,
                request.UpdatedBy);

            // Broadcast delta to all connected devices
            var deviceCount = DeviceStreamServiceImpl.GetActiveConnectionCount();
            await DeviceStreamServiceImpl.BroadcastBlacklistDeltaAsync(
                delta,
                _logger,
                _metrics,
                context.CancellationToken);

            return new BlacklistUpdateResponse
            {
                Success = true,
                Message = $"Added {delta.Added.Count} tokens to blacklist",
                DeltaSeqNo = delta.SeqNo,
                DevicesNotified = deviceCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tokens to blacklist");
            _metrics.RecordError("blacklist_add_error");

            return new BlacklistUpdateResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                DeltaSeqNo = 0,
                DevicesNotified = 0
            };
        }
    }

    public override async Task<BlacklistUpdateResponse> RemoveFromBlacklist(
        BlacklistUpdateRequest request,
        ServerCallContext context)
    {
        try
        {
            _logger.LogInformation(
                "Removing {Count} tokens from blacklist. Reason: {Reason}, By: {UpdatedBy}",
                request.PanTokens.Count, request.Reason, request.UpdatedBy);

            var delta = await _blacklistManager.RemoveFromBlacklistAsync(
                request.PanTokens,
                request.Reason,
                request.UpdatedBy);

            // Broadcast delta to all connected devices
            var deviceCount = DeviceStreamServiceImpl.GetActiveConnectionCount();
            await DeviceStreamServiceImpl.BroadcastBlacklistDeltaAsync(
                delta,
                _logger,
                _metrics,
                context.CancellationToken);

            return new BlacklistUpdateResponse
            {
                Success = true,
                Message = $"Removed {delta.Removed.Count} tokens from blacklist",
                DeltaSeqNo = delta.SeqNo,
                DevicesNotified = deviceCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tokens from blacklist");
            _metrics.RecordError("blacklist_remove_error");

            return new BlacklistUpdateResponse
            {
                Success = false,
                Message = $"Error: {ex.Message}",
                DeltaSeqNo = 0,
                DevicesNotified = 0
            };
        }
    }

    public override async Task<BlacklistSnapshot> GetBlacklist(
        BlacklistQuery request,
        ServerCallContext context)
    {
        try
        {
            // For PoC, we use deviceId = "global" to get full blacklist
            var state = await _blacklistManager.GetCurrentBlacklistAsync("global");

            if (state == null)
            {
                return new BlacklistSnapshot
                {
                    CurrentSeqNo = 0,
                    TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }

            var snapshot = new BlacklistSnapshot
            {
                CurrentSeqNo = state.CurrentSeqNo,
                TimestampUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            snapshot.PanTokens.AddRange(state.PanTokens);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blacklist");
            _metrics.RecordError("blacklist_get_error");
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }
}
