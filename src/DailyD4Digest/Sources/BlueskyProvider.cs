using System.Text.Json;
using DailyD4Digest.Models;

namespace DailyD4Digest.Sources;

public sealed class BlueskyProvider(HttpClient http, ILogger<BlueskyProvider> logger) : ISourceProvider
{
    public string Name => "Bluesky";

    private const string BaseUrl = "https://public.api.bsky.app/xrpc/app.bsky.feed.searchPosts";

    public async Task<IReadOnlyList<FeedItem>> FetchAsync(CancellationToken ct = default)
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "feeds.json");
        var json = await File.ReadAllTextAsync(configPath, ct);
        var config = JsonSerializer.Deserialize<FeedsConfig>(json);
        var queries = config?.BlueskyQueries ?? [];

        var items = new List<FeedItem>();

        foreach (var query in queries)
        {
            try
            {
                var url = $"{BaseUrl}?q={Uri.EscapeDataString(query)}&limit=20&sort=latest";
                using var response = await http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(ct);
                var doc = JsonDocument.Parse(content);

                if (!doc.RootElement.TryGetProperty("posts", out var posts))
                    continue;

                foreach (var post in posts.EnumerateArray())
                {
                    var record = post.GetProperty("record");
                    var text = record.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                    var createdAt = record.TryGetProperty("createdAt", out var dateProp)
                        ? DateTimeOffset.Parse(dateProp.GetString() ?? "")
                        : DateTimeOffset.UtcNow;

                    var author = post.TryGetProperty("author", out var authorProp)
                        ? authorProp.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : ""
                        : "";

                    var uri = post.TryGetProperty("uri", out var uriProp) ? uriProp.GetString() ?? "" : "";
                    // Convert AT URI to web URL
                    var webUrl = ConvertToWebUrl(uri, author);

                    // Extract any embedded link
                    var embeddedUrl = ExtractEmbeddedUrl(post);

                    items.Add(new FeedItem
                    {
                        Title = Truncate(text, 120),
                        Url = embeddedUrl ?? webUrl,
                        Summary = text,
                        Source = $"Bluesky (@{author})",
                        PublishedAt = createdAt,
                        Author = author,
                        Tags = ["bluesky", query]
                    });
                }

                logger.LogInformation("Bluesky '{Query}': fetched {Count} posts", query, posts.GetArrayLength());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch Bluesky for query '{Query}'", query);
            }
        }

        return items;
    }

    private static string ConvertToWebUrl(string atUri, string handle)
    {
        // at://did:plc:xxx/app.bsky.feed.post/yyy → https://bsky.app/profile/handle/post/yyy
        var parts = atUri.Split('/');
        if (parts.Length >= 5)
        {
            var postId = parts[^1];
            return $"https://bsky.app/profile/{handle}/post/{postId}";
        }
        return atUri;
    }

    private static string? ExtractEmbeddedUrl(JsonElement post)
    {
        if (!post.TryGetProperty("embed", out var embed)) return null;
        if (!embed.TryGetProperty("external", out var external)) return null;
        return external.TryGetProperty("uri", out var uri) ? uri.GetString() : null;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
