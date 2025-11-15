using DeltaList.Shared.Messages;
using DeltaList.Shared.Models;

namespace GrpcBackend.Services;

public interface IEventStore
{
    Task StoreBatchAsync(Batch batch, DateTime receivedAt);
    Task<IEnumerable<EventRecord>> GetEventsAsync(string deviceId, DateTime from, DateTime to);
    Task<long> GetEventCountAsync(string deviceId);
}
