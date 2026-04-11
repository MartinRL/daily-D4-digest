using DailyD4Digest.Models;
using Microsoft.Extensions.Logging;

namespace DailyD4Digest.Output;

public sealed class MarkdownWriter(ILogger<MarkdownWriter> logger)
{
    public async Task WriteAsync(DailyBrief brief, string outputDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        var fileName = $"{brief.Date:yyyy-MM-dd}.md";
        var filePath = Path.Combine(outputDir, fileName);

        if (File.Exists(filePath))
        {
            logger.LogInformation("Brief already exists at {Path}, skipping", filePath);
            return;
        }

        // The synthesis prompt should produce the full markdown including frontmatter.
        // If the model didn't include frontmatter, prepend it.
        var content = brief.Markdown;
        if (!content.StartsWith("---"))
        {
            var frontmatter = $"""
                ---
                tags:
                  - daily-D4-digest
                  - agentic-engineering
                  - ai-research
                date: {brief.Date:yyyy-MM-dd}
                sources_scanned: {brief.SourcesScanned}
                items_scored: {brief.ItemsScored}
                items_selected: {brief.ItemsSelected}
                ---

                """;
            content = frontmatter + content;
        }

        await File.WriteAllTextAsync(filePath, content, ct);
        logger.LogInformation("Wrote daily brief to {Path}", filePath);
    }
}
