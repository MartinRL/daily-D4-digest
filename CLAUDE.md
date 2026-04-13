# CLAUDE.md

## What This Is

A daily automated intelligence brief on **agentic engineering**, analyzed through the **D1-D4 framework** and the **Software Civil Engineering** thesis. Built as a C# .NET 10 console app, served via Quartz static site on GitHub Pages.

## Architecture

```
Sources (RSS, arXiv, Reddit, Bluesky)
  → Collect → Dedup → Score (Sonnet) → Enrich → Synthesize (Opus) → Write
  → content/briefs/YYYY-MM-DD.md
  → Quartz → GitHub Pages
```

### Key Directories

| Path | Purpose |
|------|---------|
| `src/DailyD4Digest/` | .NET console app — the pipeline |
| `src/DailyD4Digest/Config/` | feeds.json, dimensions.json, prompts/ |
| `content/briefs/` | Generated daily markdown briefs |
| `content/` | Quartz content root (also works as Obsidian vault) |
| `.github/workflows/` | Daily cron + Ralph loop (claude-code-action) |

### Data Sources

All free, no API keys (except Anthropic for AI):
- **RSS feeds** — blogs (Willison, Fowler, Latent Space), HN filtered, InfoQ, arXiv daily
- **arXiv API** — search by category + keywords (cs.SE, cs.AI, cs.MA)
- **Reddit JSON** — top posts from relevant subreddits (no auth needed)
- **Bluesky API** — public search (no auth needed)

### Models

- **Sonnet 4.6** — scoring (structured JSON, fast, batch processing)
- **Opus 4.6** — synthesis (deeper analysis, SCE cross-cutting lens)

### The D1-D4 Framework

| | Internal | External |
|---|---|---|
| Building | **D1: Agentic Engineering** | **D2: AI in the Product** |
| Scaling | **D4: Performance & Cost** | **D3: Build for Agents** |

### Software Civil Engineering (SCE)

The thesis: agentic AI is the forcing function for software's professionalization. Event Modeling as specification language, Decider pattern for simulation, Specify → Plan → Verify → Apply → Observe lifecycle.

## Conventions

- Conventional commits (`docs:`, `feat:`, `fix:`, `chore:`)
- Output is Obsidian-flavored Markdown (frontmatter, callouts, wikilinks)
- Config changes go in `feeds.json` or `dimensions.json`, not in code
- Prompts live in `Config/prompts/` as `.md` files — edit freely
- `.slnx` solution format (not `.sln`)

## Running Locally

```bash
export ANTHROPIC_API_KEY=sk-ant-...
dotnet run --project src/DailyD4Digest
```

Output: `content/briefs/YYYY-MM-DD.md`
