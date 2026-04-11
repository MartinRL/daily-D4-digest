using System.Text.Json;
using Anthropic;
using DailyD4Digest.Models;

namespace DailyD4Digest.Scoring;

public sealed class RelevanceScorer(ILogger<RelevanceScorer> logger)
{
    private const int BatchSize = 15;
    private const int MinScore = 3; // Items must score >= 3 on at least one dimension

    public async Task<IReadOnlyList<ScoredItem>> ScoreAsync(
        IReadOnlyList<FeedItem> items,
        CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not set");

        var client = new AnthropicClient(new APIKeyAuth(apiKey));

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Config", "prompts", "scoring.md");
        var systemPrompt = await File.ReadAllTextAsync(promptPath, ct);

        var scored = new List<ScoredItem>();

        // Process in batches
        var batches = items.Chunk(BatchSize);
        foreach (var batch in batches)
        {
            try
            {
                var batchJson = JsonSerializer.Serialize(batch.Select(i => new
                {
                    i.Title,
                    i.Summary,
                    i.Source,
                    i.Url
                }));

                var response = await client.Messages.CreateAsync(new()
                {
                    Model = "claude-sonnet-4-6",
                    MaxTokens = 4096,
                    System = [new() { Text = systemPrompt }],
                    Messages = [new()
                    {
                        Role = "user",
                        Content = [new() { Text = $"Score the following items:\n\n{batchJson}" }]
                    }]
                }, ct);

                var responseText = response.Content
                    .Where(c => c.Text is not null)
                    .Select(c => c.Text)
                    .FirstOrDefault() ?? "[]";

                // Extract JSON from response (may be wrapped in markdown code block)
                var jsonText = ExtractJson(responseText);
                var scores = JsonSerializer.Deserialize<List<ScoreResult>>(jsonText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

                for (var i = 0; i < Math.Min(batch.Length, scores.Count); i++)
                {
                    var score = scores[i];
                    var item = new ScoredItem
                    {
                        Item = batch[i],
                        D1Score = score.D1,
                        D2Score = score.D2,
                        D3Score = score.D3,
                        D4Score = score.D4,
                        SceScore = score.Sce
                    };

                    if (item.MaxScore >= MinScore)
                        scored.Add(item);
                }

                logger.LogInformation("Scored batch of {Count} items, {Kept} kept", batch.Length, scored.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to score batch of {Count} items", batch.Length);
            }
        }

        return scored.OrderByDescending(s => s.MaxScore).ToList();
    }

    private static string ExtractJson(string text)
    {
        // Handle markdown code blocks
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return text;
    }
}

internal sealed record ScoreResult
{
    public int D1 { get; init; }
    public int D2 { get; init; }
    public int D3 { get; init; }
    public int D4 { get; init; }
    public int Sce { get; init; }
}
