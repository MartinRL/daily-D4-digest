using System.Text.Json;
using DailyD4Digest.Models;

namespace DailyD4Digest.Sources;

public sealed class RedditProvider(HttpClient http, ILogger<RedditProvider> logger) : ISourceProvider
{
    public string Name => "Reddit";

    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct = default)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "feeds.json");
        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<FeedsConfig>(json);
        var subreddits = config?.RedditSubreddits ?? [];

        var items = new List<FeedItem>();

        foreach (var subreddit in subreddits)
        {
            try
            {
                var url = $"https://www.reddit.com/r/{subreddit}/top.json?t=day&limit=10";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "DailyD4Digest/1.0 (research bot)");

                using var response = await http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(content);

                var children = doc.RootElement
                    .GetProperty("data")
                    .GetProperty("children");

                foreach (var child in children.EnumerateArray())
                {
                    var data = child.GetProperty("data");
                    var createdUtc = data.GetProperty("created_utc").GetDouble();
                    var publishedAt = DateTimeOffset.FromUnixTimeSeconds((long)createdUtc);

                    items.Add(new FeedItem
                    {
                        Title = data.GetProperty("title").GetString() ?? "",
                        Url = data.TryGetProperty("url", out var urlProp)
                            ? urlProp.GetString() ?? $"https://reddit.com{data.GetProperty("permalink").GetString()}"
                            : $"https://reddit.com{data.GetProperty("permalink").GetString()}",
                        Summary = data.TryGetProperty("selftext", out var selftext)
                            ? Truncate(selftext.GetString() ?? "", 500)
                            : "",
                        Source = $"r/{subreddit}",
                        PublishedAt = publishedAt,
                        Author = data.TryGetProperty("author", out var author) ? author.GetString() ?? "" : "",
                        Tags = [$"reddit", subreddit]
                    });
                }

                logger.LogInformation("Reddit r/{Subreddit}: fetched {Count} posts", subreddit, children.GetArrayLength());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch Reddit r/{Subreddit}", subreddit);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct); // Be polite to Reddit
        }

        return items;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
