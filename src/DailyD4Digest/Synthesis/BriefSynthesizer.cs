using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using DailyD4Digest.Models;
using Microsoft.Extensions.Logging;

namespace DailyD4Digest.Synthesis;

public sealed class BriefSynthesizer(ILogger<BriefSynthesizer> logger)
{
    public async Task<DailyBrief> SynthesizeAsync(
        IReadOnlyList<ScoredItem> items,
        int totalScanned,
        int totalScored,
        CancellationToken ct = default)
    {
        _ = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not set");

        var client = new AnthropicClient();

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Config", "prompts", "synthesis.md");
        var systemPrompt = await File.ReadAllTextAsync(promptPath, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var itemsJson = JsonSerializer.Serialize(items.Select(i => new
        {
            i.Item.Title,
            i.Item.Url,
            i.Item.Summary,
            i.Item.Source,
            i.Item.Author,
            i.D1Score,
            i.D2Score,
            i.D3Score,
            i.D4Score,
            i.SceScore,
            EnrichedContent = i.EnrichedContent.Length > 0
                ? i.EnrichedContent[..Math.Min(i.EnrichedContent.Length, 2000)]
                : ""
        }), new JsonSerializerOptions { WriteIndented = true });

        var userMessage = $"""
            Today is {today:yyyy-MM-dd}.
            Stats: {totalScanned} sources scanned, {totalScored} items scored, {items.Count} selected.

            Generate the daily D4 digest from these scored and enriched items:

            {itemsJson}
            """;

        logger.LogInformation("Synthesizing brief with {Count} items via Opus", items.Count);

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = "claude-opus-4-6",
            MaxTokens = 8192,
            System = systemPrompt,
            Messages = [new()
            {
                Role = Role.User,
                Content = userMessage,
            }]
        }, ct);

        var markdown = string.Join("", response.Content
            .Select(block => block.TryPickText(out var text) ? text.Text : ""));

        markdown = StripCodeFences(markdown);

        return new DailyBrief
        {
            Date = today,
            Markdown = markdown,
            SourcesScanned = totalScanned,
            ItemsScored = totalScored,
            ItemsSelected = items.Count
        };
    }

    private static string StripCodeFences(string text)
    {
        if (text.StartsWith("```markdown", StringComparison.OrdinalIgnoreCase))
            text = text["```markdown".Length..];
        else if (text.StartsWith("```md", StringComparison.OrdinalIgnoreCase))
            text = text["```md".Length..];
        else if (text.StartsWith("```"))
            text = text[3..];

        if (text.EndsWith("```"))
            text = text[..^3];

        return text.Trim();
    }
}
