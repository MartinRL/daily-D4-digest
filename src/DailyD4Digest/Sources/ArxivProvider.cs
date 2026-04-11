using System.ServiceModel.Syndication;
using System.Xml;
using DailyD4Digest.Models;
using Microsoft.Extensions.Logging;

namespace DailyD4Digest.Sources;

public sealed class ArxivProvider(HttpClient http, ILogger<ArxivProvider> logger) : ISourceProvider
{
    public string Name => "arXiv";

    private const string BaseUrl = "https://export.arxiv.org/api/query";
    private static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct = default)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "feeds.json");
        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = System.Text.Json.JsonSerializer.Deserialize<FeedsConfig>(json);
        var queries = config?.ArxivQueries ?? [];

        var items = new List<FeedItem>();

        foreach (var query in queries)
        {
            var searchQuery = $"cat:{query.Category}+AND+({query.Keywords})";
            var url = $"{BaseUrl}?search_query={Uri.EscapeDataString(searchQuery)}&sortBy=submittedDate&sortOrder=descending&max_results=20";

            try
            {
                using var stream = await http.GetStreamAsync(url, ct);
                using var reader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(reader);

                var cutoff = DateTimeOffset.UtcNow.AddHours(-48); // Papers may appear with delay

                var feedItems = feed.Items
                    .Where(i => i.PublishDate >= cutoff)
                    .Select(i => new FeedItem
                    {
                        Title = i.Title?.Text ?? "",
                        Url = i.Links.FirstOrDefault(l => l.RelationshipType == "alternate")?.Uri?.AbsoluteUri
                              ?? i.Id ?? "",
                        Summary = i.Summary?.Text ?? "",
                        Source = $"arXiv:{query.Category}",
                        PublishedAt = i.PublishDate,
                        Author = string.Join(", ", i.Authors.Select(a => a.Name)),
                        Tags = [query.Category]
                    })
                    .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                    .ToList();

                items.AddRange(feedItems);
                logger.LogInformation("arXiv {Category}: fetched {Count} papers", query.Category, feedItems.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch arXiv for {Category}", query.Category);
            }

            await Task.Delay(RateLimit, ct); // Respect arXiv rate limit
        }

        return items;
    }
}
