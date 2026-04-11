using DailyD4Digest.Models;

namespace DailyD4Digest.Sources;

public interface ISourceProvider
{
    string Name { get; }
    Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct = default);
}
