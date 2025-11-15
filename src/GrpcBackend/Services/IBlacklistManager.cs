using DeltaList.Shared.Models;
using DeltaList.Shared.Messages;

namespace GrpcBackend.Services;

public interface IBlacklistManager
{
    Task<BlacklistState?> GetCurrentBlacklistAsync(string deviceId);
    Task<BlacklistDelta> AddToBlacklistAsync(IEnumerable<string> panTokens, string reason, string updatedBy);
    Task<BlacklistDelta> RemoveFromBlacklistAsync(IEnumerable<string> panTokens, string reason, string updatedBy);
    Task<ulong> GetNextSequenceNumberAsync();
}
