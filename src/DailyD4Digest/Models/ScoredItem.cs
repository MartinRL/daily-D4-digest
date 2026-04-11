namespace DailyD4Digest.Models;

/// <summary>
/// A feed item with relevance scores per dimension.
/// </summary>
public sealed record ScoredItem
{
    public required FeedItem Item { get; init; }
    public int D1Score { get; init; }
    public int D2Score { get; init; }
    public int D3Score { get; init; }
    public int D4Score { get; init; }
    public int SceScore { get; init; }

    public int MaxScore => Math.Max(Math.Max(Math.Max(D1Score, D2Score), Math.Max(D3Score, D4Score)), SceScore);

    public string EnrichedContent { get; init; } = "";
}
