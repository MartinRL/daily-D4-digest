using System.ServiceModel.Syndication;
using System.Xml;
using DailyD4Digest.Models;
using Microsoft.Extensions.Logging;

namespace DailyD4Digest.Sources;

public sealed class RssFeedProvider(HttpClient http, ILogger<RssFeedProvider> logger) : ISourceProvider
{
    public string Name => "RSS";

    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct = default)
    {
        var feedUrls = await LoadFeedUrlsAsync(ct);
        var items = new List<FeedItem>();

        var tasks = feedUrls.Select(url => FetchFeedAsync(url, ct));
        var results = await Task.WhenAll(tasks);

        foreach (var batch in results)
            items.AddRange(batch);

        logger.LogInformation("RSS: fetched {Count} items from {FeedCount} feeds", items.Count, feedUrls.Count);
        return items;
    }

    private async Task<IReadOnlyList<FeedItem>> FetchFeedAsync(string url, CancellationToken ct)
    {
        try
        {
            using var stream = await http.GetStreamAsync(url, ct);
            using var reader = XmlReader.Create(stream);
            var feed = SyndicationFeed.Load(reader);

            var cutoff = DateTimeOffset.UtcNow.AddHours(-36); // 36h window for timezone safety

            return feed.Items
                .Where(i => i.PublishDate >= cutoff)
                .Select(i => new FeedItem
                {
                    Title = i.Title?.Text ?? "",
                    Url = i.Links.FirstOrDefault()?.Uri?.AbsoluteUri ?? "",
                    Summary = i.Summary?.Text ?? "",
                    Source = feed.Title?.Text ?? url,
                    PublishedAt = i.PublishDate,
                    Author = i.Authors.FirstOrDefault()?.Name ?? "",
                    Tags = i.Categories.Select(c => c.Name).ToList()
                })
                .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch RSS feed: {Url}", url);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> LoadFeedUrlsAsync(CancellationToken ct)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "feeds.json");
        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = System.Text.Json.JsonSerializer.Deserialize<FeedsConfig>(json)
            ?? throw new InvalidOperationException("Failed to parse feeds.json");
        return config.RssFeeds;
    }
}

internal sealed record FeedsConfig
{
    public List<string> RssFeeds { get; init; } = [];
    public List<string> RedditSubreddits { get; init; } = [];
    public List<string> BlueskyQueries { get; init; } = [];
    public List<ArxivQueryConfig> ArxivQueries { get; init; } = [];
}

internal sealed record ArxivQueryConfig
{
    public string Category { get; init; } = "";
    public string Keywords { get; init; } = "";
}
