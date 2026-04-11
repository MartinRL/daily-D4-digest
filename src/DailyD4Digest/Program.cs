using DailyD4Digest.Models;
using DailyD4Digest.Output;
using DailyD4Digest.Scoring;
using DailyD4Digest.Sources;
using DailyD4Digest.Synthesis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddTransient<RssFeedProvider>();
builder.Services.AddTransient<ArxivProvider>();
builder.Services.AddTransient<RedditProvider>();
builder.Services.AddTransient<BlueskyProvider>();
builder.Services.AddTransient<RelevanceScorer>();
builder.Services.AddTransient<BriefSynthesizer>();
builder.Services.AddTransient<MarkdownWriter>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DailyD4Digest");
var ct = CancellationToken.None;

// Determine output directory — default to content/briefs/ relative to repo root
var outputDir = Environment.GetEnvironmentVariable("OUTPUT_DIR")
    ?? Path.Combine(FindRepoRoot(), "content", "briefs");

logger.LogInformation("Starting Daily D4 Digest pipeline");
logger.LogInformation("Output directory: {OutputDir}", outputDir);

// Check idempotency
var todayFile = Path.Combine(outputDir, $"{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.md");
if (File.Exists(todayFile))
{
    logger.LogInformation("Today's brief already exists at {Path}, exiting", todayFile);
    return;
}

// 1. COLLECT — fetch from all sources in parallel
logger.LogInformation("Step 1/6: COLLECT");
var httpFactory = host.Services.GetRequiredService<IHttpClientFactory>();

var providers = new ISourceProvider[]
{
    host.Services.GetRequiredService<RssFeedProvider>(),
    host.Services.GetRequiredService<ArxivProvider>(),
    host.Services.GetRequiredService<RedditProvider>(),
    host.Services.GetRequiredService<BlueskyProvider>()
};

var fetchTasks = providers.Select(p => p.FetchAsync(ct));
var results = await Task.WhenAll(fetchTasks);
var allItems = results.SelectMany(r => r).ToList();
var totalScanned = allItems.Count;

logger.LogInformation("Collected {Count} items from {ProviderCount} providers", totalScanned, providers.Length);

if (allItems.Count == 0)
{
    logger.LogWarning("No items collected from any source. Exiting.");
    return;
}

// 2. DEDUP — normalize titles, remove duplicates
logger.LogInformation("Step 2/6: DEDUP");
var deduped = allItems
    .GroupBy(i => NormalizeTitle(i.Title))
    .Select(g => g.OrderByDescending(i => i.PublishedAt).First())
    .ToList();

logger.LogInformation("Deduped {Before} → {After} items", allItems.Count, deduped.Count);

// 3. SCORE — relevance scoring via Sonnet
logger.LogInformation("Step 3/6: SCORE");
var scorer = host.Services.GetRequiredService<RelevanceScorer>();
var scored = await scorer.ScoreAsync(deduped, ct);
var totalScored = deduped.Count;

logger.LogInformation("Scored {Count} items, {Kept} passed threshold", totalScored, scored.Count);

if (scored.Count == 0)
{
    logger.LogWarning("No items passed relevance threshold. Exiting.");
    return;
}

// 4. ENRICH — fetch full content for top items
logger.LogInformation("Step 4/6: ENRICH");
var topItems = scored.Take(12).ToList();
var http = httpFactory.CreateClient();
var enriched = new List<ScoredItem>();

foreach (var item in topItems)
{
    var enrichedContent = "";
    if (!string.IsNullOrWhiteSpace(item.Item.Url) && Uri.IsWellFormedUriString(item.Item.Url, UriKind.Absolute))
    {
        try
        {
            var response = await http.GetStringAsync(item.Item.Url, ct);
            // Take first 3000 chars of the page as enrichment context
            enrichedContent = response.Length > 3000 ? response[..3000] : response;
        }
        catch
        {
            // Enrichment is best-effort
        }
    }
    enriched.Add(item with { EnrichedContent = enrichedContent });
}

// 5. SYNTHESIZE — produce markdown via Opus
logger.LogInformation("Step 5/6: SYNTHESIZE");
var synthesizer = host.Services.GetRequiredService<BriefSynthesizer>();
var brief = await synthesizer.SynthesizeAsync(enriched, totalScanned, totalScored, ct);

// 6. WRITE — output to content/briefs/
logger.LogInformation("Step 6/6: WRITE");
var writer = host.Services.GetRequiredService<MarkdownWriter>();
await writer.WriteAsync(brief, outputDir, ct);

logger.LogInformation("Pipeline complete!");

static string NormalizeTitle(string title)
    => new string(title.ToLowerInvariant()
        .Where(c => char.IsLetterOrDigit(c) || c == ' ')
        .ToArray())
        .Trim();

static string FindRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName;
    }
    // Fallback: assume we're running from the repo root
    return Directory.GetCurrentDirectory();
}
