namespace DailyD4Digest.Models;

/// <summary>
/// Structured daily brief ready for markdown rendering.
/// </summary>
public sealed record DailyBrief
{
    public required DateOnly Date { get; init; }
    public required string Markdown { get; init; }
    public int SourcesScanned { get; init; }
    public int ItemsScored { get; init; }
    public int ItemsSelected { get; init; }
}
