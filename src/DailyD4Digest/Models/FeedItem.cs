namespace DailyD4Digest.Models;

/// <summary>
/// Normalized item from any source (RSS, arXiv, Reddit, Bluesky).
/// </summary>
public sealed record FeedItem
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string Summary { get; init; } = "";
    public string Source { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public string Author { get; init; } = "";
    public List<string> Tags { get; init; } = [];
}
