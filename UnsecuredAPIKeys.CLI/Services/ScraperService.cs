using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Common;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers;
using UnsecuredAPIKeys.Providers._Interfaces;
using UnsecuredAPIKeys.Providers.Exceptions;
using UnsecuredAPIKeys.Providers.Search_Providers;

namespace UnsecuredAPIKeys.CLI.Services;

/// <summary>
/// Scraper service for finding API keys on GitHub.
/// Lite version: GitHub only, 3 AI providers.
/// Full version: www.UnsecuredAPIKeys.com
/// </summary>
public class ScraperService
{
    private readonly DBContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScraperService>? _logger;
    private readonly IReadOnlyList<IApiKeyProvider> _providers;
    private CancellationTokenSource? _cancellationTokenSource;
    private TokenRotationService? _tokenRotation;
    private readonly ScannedRepoCache _scannedRepoCache = new();

    private int _newKeysFound;
    private int _duplicateKeysFound;
    private int _skippedRepos;

    public ScraperService(DBContext dbContext, IHttpClientFactory httpClientFactory, ILogger<ScraperService>? logger = null)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _providers = ApiProviderRegistry.ScraperProviders;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AnsiConsole.MarkupLine("[cyan]Starting GitHub scraper...[/]");
        AnsiConsole.MarkupLine($"[dim]Loaded {_providers.Count} API key providers[/]");

        foreach (var provider in _providers)
        {
            AnsiConsole.MarkupLine($"  [dim]- {Markup.Escape(provider.ProviderName)}[/]");
        }

        // Load multiple tokens for rotation (higher rate limits)
        _tokenRotation = new TokenRotationService(_dbContext, _logger);
        await _tokenRotation.LoadTokensAsync(SearchProviderEnum.GitHub);

        if (_tokenRotation.TokenCount == 0)
        {
            AnsiConsole.MarkupLine("[red]No GitHub tokens configured. Use 'Configure Settings' to add one.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[dim]Loaded {_tokenRotation.TokenCount} GitHub tokens for rotation ({_tokenRotation.TokenCount * 30} searches/min)[/]");

        // Run continuously
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Get next token from rotation
                var token = _tokenRotation.GetNextToken();
                if (token == null)
                {
                    AnsiConsole.MarkupLine("[red]No tokens available.[/]");
                    break;
                }

                await RunScrapingCycleAsync(token);

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Time-based delay: faster during peak hours (9am-6pm UTC), slower at night
                var delayMs = GetAdaptiveDelayMs();
                var delaySec = delayMs / 1000;
                AnsiConsole.MarkupLine($"[dim]Waiting {delaySec}s before next search (adaptive)...[/]");
                await Task.Delay(delayMs, _cancellationTokenSource.Token);

                // Reset counters
                _newKeysFound = 0;
                _duplicateKeysFound = 0;
                _skippedRepos = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error during scraping: {Markup.Escape(ex.Message)}[/]");
                _logger?.LogError(ex, "Scraping cycle error");
                await Task.Delay(5000, _cancellationTokenSource.Token);
            }
        }

        // Save token usage stats
        if (_tokenRotation != null)
        {
            await _tokenRotation.SaveUsageStatsAsync();
        }

        AnsiConsole.MarkupLine("[green]Scraper stopped.[/]");
    }

    private async Task RunScrapingCycleAsync(SearchProviderToken token)
    {
        // Get next query to process - prioritize high-value queries first
        var cutoff = DateTime.UtcNow.AddMilliseconds(-LiteLimits.SearchDelayMs * 2);
        var query = await GetNextHighValueQueryAsync(cutoff);

        if (query == null)
        {
            AnsiConsole.MarkupLine("[dim]No queries due for search. Waiting...[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[cyan]Searching: {Markup.Escape(query.Query)}[/]");

        // Update last search time
        query.LastSearchUTC = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(_cancellationTokenSource!.Token);

        // Search using all available sources
        var allResults = new List<RepoReference>();

        // 1. GitHub Code Search (primary source)
        var codeSearch = new GitHubSearchProvider(_dbContext);
        try
        {
            var codeResults = await codeSearch.SearchAsync(query, token);
            if (codeResults != null)
                allResults.AddRange(codeResults);
        }
        catch (SearchRateLimitException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]GitHub code search rate limited. {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Code search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 2. GitHub Gist Search (keys often pasted in gists)
        var gistSearch = new GitHubGistSearchProvider(_dbContext);
        try
        {
            var gistResults = await gistSearch.SearchAsync(query, token);
            if (gistResults != null)
                allResults.AddRange(gistResults);
        }
        catch (SearchRateLimitException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]GitHub gist search rate limited. {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Gist search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 3. GitHub Issues/PRs (users paste keys when asking for help)
        var issuesSearch = new GitHubIssuesSearchProvider(_dbContext);
        try
        {
            var issuesResults = await issuesSearch.SearchAsync(query, token);
            if (issuesResults != null)
                allResults.AddRange(issuesResults);
        }
        catch (SearchRateLimitException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]GitHub issues search rate limited. {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Issues search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 4. GitLab Search (different codebase)
        var gitlabSearch = new GitLabSearchProvider(_dbContext);
        try
        {
            var gitlabResults = await gitlabSearch.SearchAsync(query, token);
            if (gitlabResults != null)
                allResults.AddRange(gitlabResults);
        }
        catch (SearchRateLimitException ex)
        {
            AnsiConsole.MarkupLine($"[yellow]GitLab search rate limited. {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]GitLab search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 5. GitLab Snippets (like GitHub Gists)
        var snippetsSearch = new GitLabSnippetsSearchProvider(_dbContext);
        try
        {
            var snippetsResults = await snippetsSearch.SearchAsync(query, token);
            if (snippetsResults != null)
                allResults.AddRange(snippetsResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]GitLab snippets error: {Markup.Escape(ex.Message)}[/]");
        }

        // 6. Stack Overflow (code snippets with keys)
        var soSearch = new StackOverflowSearchProvider(_dbContext);
        try
        {
            var soResults = await soSearch.SearchAsync(query, token);
            if (soResults != null)
                allResults.AddRange(soResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Stack Overflow search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 7. Pastebin (public pastes with configs)
        var pastebinSearch = new PastebinSearchProvider(_dbContext);
        try
        {
            var pastebinResults = await pastebinSearch.SearchAsync(query, token);
            if (pastebinResults != null)
                allResults.AddRange(pastebinResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Pastebin search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 8. Hugging Face (AI model configs with API keys)
        var hfSearch = new HuggingFaceSearchProvider(_dbContext);
        try
        {
            var hfResults = await hfSearch.SearchAsync(query, token);
            if (hfResults != null)
                allResults.AddRange(hfResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Hugging Face search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 9. Reddit (people share keys in AI subreddits)
        var redditSearch = new RedditSearchProvider(_dbContext);
        try
        {
            var redditResults = await redditSearch.SearchAsync(query, token);
            if (redditResults != null)
                allResults.AddRange(redditResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Reddit search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 10. Replicate (model deployment configs)
        var replicateSearch = new ReplicateSearchProvider(_dbContext);
        try
        {
            var replicateResults = await replicateSearch.SearchAsync(query, token);
            if (replicateResults != null)
                allResults.AddRange(replicateResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Replicate search error: {Markup.Escape(ex.Message)}[/]");
        }

        // 11. GitHub Event Monitor (real-time commits with keys)
        var eventMonitor = new GitHubEventMonitor(_dbContext);
        try
        {
            var eventResults = await eventMonitor.SearchAsync(query, token);
            if (eventResults != null)
                allResults.AddRange(eventResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]GitHub event monitor error: {Markup.Escape(ex.Message)}[/]");
        }

        // 12. Leak Database Search (known leaked keys)
        var leakSearch = new LeakDatabaseSearchProvider(_dbContext);
        try
        {
            var leakResults = await leakSearch.SearchAsync(query, token);
            if (leakResults != null)
                allResults.AddRange(leakResults);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Leak database error: {Markup.Escape(ex.Message)}[/]");
        }

        if (allResults.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No results from search.[/]");
            return;
        }

        AnsiConsole.MarkupLine($"[dim]Found {allResults.Count} potential matches[/]");

        // Process results in parallel (5 concurrent) for speed
        var semaphore = new System.Threading.SemaphoreSlim(5);
        var processedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"[cyan]Processing results[/]", maxValue: allResults.Count);

                var tasks = allResults.Select(async repoRef =>
                {
                    if (_cancellationTokenSource!.Token.IsCancellationRequested)
                        return;

                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        await ProcessResultAsync(repoRef, token, query);
                        Interlocked.Increment(ref processedCount);
                        task.Increment(1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            });

        // Summary
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Query", Markup.Escape(query.Query));
        table.AddRow("Results Processed", processedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        table.AddRow("Skipped (cached)", $"[dim]{_skippedRepos}[/]");
        table.AddRow("New Keys", $"[green]{_newKeysFound}[/]");
        table.AddRow("Duplicates", $"[dim]{_duplicateKeysFound}[/]");
        table.AddRow("Cache Size", $"[dim]{_scannedRepoCache.Count} repos[/]");

        AnsiConsole.Write(table);
    }

    /// <summary>
    /// Gets the next query to process, prioritizing high-value queries first.
    /// High-value queries target specific file types (.env, config) and use freshness filters.
    /// </summary>
    private async Task<SearchQuery?> GetNextHighValueQueryAsync(DateTime cutoff)
    {
        // Priority 1: Queries with filename: qualifier (highest hit rate)
        var highValueQuery = await _dbContext.SearchQueries
            .Where(x => x.IsEnabled && x.LastSearchUTC < cutoff && x.Query.Contains("filename:"))
            .OrderBy(x => x.LastSearchUTC)
            .FirstOrDefaultAsync(_cancellationTokenSource!.Token);

        if (highValueQuery != null)
            return highValueQuery;

        // Priority 2: Queries with created: qualifier (recent keys more likely valid)
        var recentQuery = await _dbContext.SearchQueries
            .Where(x => x.IsEnabled && x.LastSearchUTC < cutoff && x.Query.Contains("created:"))
            .OrderBy(x => x.LastSearchUTC)
            .FirstOrDefaultAsync(_cancellationTokenSource!.Token);

        if (recentQuery != null)
            return recentQuery;

        // Priority 3: Queries with language: qualifier
        var langQuery = await _dbContext.SearchQueries
            .Where(x => x.IsEnabled && x.LastSearchUTC < cutoff && x.Query.Contains("language:"))
            .OrderBy(x => x.LastSearchUTC)
            .FirstOrDefaultAsync(_cancellationTokenSource!.Token);

        if (langQuery != null)
            return langQuery;

        // Priority 4: Any remaining query
        return await _dbContext.SearchQueries
            .Where(x => x.IsEnabled && x.LastSearchUTC < cutoff)
            .OrderBy(x => x.LastSearchUTC)
            .FirstOrDefaultAsync(_cancellationTokenSource!.Token);
    }

    private async Task ProcessResultAsync(RepoReference repoRef, SearchProviderToken token, SearchQuery query)
    {
        try
        {
            // Skip low-value files before fetching content
            if (IsLowValueFile(repoRef.FileName, repoRef.FilePath))
                return;

            // Skip repos we've already scanned recently
            if (!string.IsNullOrEmpty(repoRef.RepoOwner) && !string.IsNullOrEmpty(repoRef.RepoName))
            {
                if (_scannedRepoCache.IsScanned(repoRef.RepoOwner, repoRef.RepoName))
                {
                    Interlocked.Increment(ref _skippedRepos);
                    return;
                }
            }

            // Use cached content if available (from Gist/GitLab search), otherwise fetch
            var content = repoRef._cachedContent ?? await FetchFileContentAsync(repoRef, token);

            if (string.IsNullOrEmpty(content))
                return;

            // Skip files that are too large (>100KB) to save time
            if (content.Length > 100_000)
                return;

            // Mark repo as scanned
            if (!string.IsNullOrEmpty(repoRef.RepoOwner) && !string.IsNullOrEmpty(repoRef.RepoName))
            {
                _scannedRepoCache.MarkScanned(repoRef.RepoOwner, repoRef.RepoName);
            }

            // Search for API keys using all provider patterns
            foreach (var provider in _providers)
            {
                foreach (var regex in provider.CompiledRegexes)
                {
                    var matches = regex.Matches(content);

                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        var apiKey = match.Value;

                        // Validate key format before saving (reduces junk)
                        if (apiKey.Length < 20 || apiKey.Length > 200)
                            continue;

                        // Skip obvious placeholder/test keys
                        if (IsPlaceholderKey(apiKey))
                            continue;

                        // Check if already exists
                        var exists = await _dbContext.APIKeys
                            .AnyAsync(k => k.ApiKey == apiKey, _cancellationTokenSource!.Token);

                        if (exists)
                        {
                            Interlocked.Increment(ref _duplicateKeysFound);
                            continue;
                        }

                        // Add new key
                        var newKey = new APIKey
                        {
                            ApiKey = apiKey,
                            ApiType = provider.ApiType,
                            Status = ApiStatusEnum.Unverified,
                            SearchProvider = SearchProviderEnum.GitHub,
                            FirstFoundUTC = DateTime.UtcNow,
                            LastFoundUTC = DateTime.UtcNow
                        };

                        // Add repo reference
                        repoRef.SearchQueryId = query.Id;
                        repoRef.FoundUTC = DateTime.UtcNow;
                        repoRef.Provider = "GitHub";
                        newKey.References.Add(repoRef);

                        _dbContext.APIKeys.Add(newKey);
                        await _dbContext.SaveChangesAsync(_cancellationTokenSource!.Token);

                        Interlocked.Increment(ref _newKeysFound);
                        AnsiConsole.MarkupLine($"[green]+ New {Markup.Escape(provider.ProviderName)} key found![/]");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error processing result: {Url}", repoRef.FileURL!);
        }
    }

    /// <summary>
    /// Filters out low-value files that are unlikely to contain real API keys.
    /// </summary>
    private static bool IsLowValueFile(string? fileName, string? filePath)
    {
        var name = (fileName ?? "").ToLowerInvariant();
        var path = (filePath ?? "").ToLowerInvariant();

        // Documentation files
        if (name.EndsWith(".md", StringComparison.Ordinal) || name.EndsWith(".rst", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal))
            return true;

        // Test files
        if (path.Contains("/test/", StringComparison.Ordinal) || path.Contains("/tests/", StringComparison.Ordinal) || path.Contains("/__tests__/", StringComparison.Ordinal) ||
            name.StartsWith("test_", StringComparison.Ordinal) || name.EndsWith("_test.", StringComparison.Ordinal) || name.EndsWith(".test.", StringComparison.Ordinal) ||
            name.Contains("spec.", StringComparison.Ordinal) || name.Contains("_spec.", StringComparison.Ordinal))
            return true;

        // Example/sample/demo files
        if (path.Contains("/example", StringComparison.Ordinal) || path.Contains("/sample", StringComparison.Ordinal) || path.Contains("/demo/", StringComparison.Ordinal) ||
            path.Contains("/fixtures/", StringComparison.Ordinal) || path.Contains("/mocks/", StringComparison.Ordinal) ||
            name.Contains("example", StringComparison.Ordinal) || name.Contains("sample", StringComparison.Ordinal) || name.Contains("demo", StringComparison.Ordinal))
            return true;

        // Lock files and generated files
        if (name.EndsWith(".lock", StringComparison.Ordinal) || name.EndsWith("lock.json", StringComparison.Ordinal) ||
            name.Contains(".min.js", StringComparison.Ordinal) || name.Contains(".min.css", StringComparison.Ordinal) ||
            name.Contains(".bundle.", StringComparison.Ordinal) || name.Contains(".compiled.", StringComparison.Ordinal))
            return true;

        // Binary and asset files
        if (name.EndsWith(".png", StringComparison.Ordinal) || name.EndsWith(".jpg", StringComparison.Ordinal) || name.EndsWith(".gif", StringComparison.Ordinal) ||
            name.EndsWith(".ico", StringComparison.Ordinal) || name.EndsWith(".svg", StringComparison.Ordinal) || name.EndsWith(".woff", StringComparison.Ordinal) ||
            name.EndsWith(".ttf", StringComparison.Ordinal) || name.EndsWith(".eot", StringComparison.Ordinal) || name.EndsWith(".mp4", StringComparison.Ordinal) ||
            name.EndsWith(".mp3", StringComparison.Ordinal) || name.EndsWith(".zip", StringComparison.Ordinal) || name.EndsWith(".tar.gz", StringComparison.Ordinal))
            return true;

        // README and license files
        if (name == "readme.md" || name == "license" || name == "license.md" ||
            name == "changelog.md" || name == "contributing.md")
            return true;

        // Package manager files
        if (name == "package-lock.json" || name == "yarn.lock" || name == "pnpm-lock.yaml" ||
            name == "composer.lock" || name == "poetry.lock" || name == "Cargo.lock")
            return true;

        // IDE and editor configs
        if (name == ".gitignore" || name == ".editorconfig" || name == ".eslintrc" ||
            name == ".prettierrc" || name == "tsconfig.json" || name == ".vscode")
            return true;

        return false;
    }

    /// <summary>
    /// Detects obvious placeholder/test API keys that aren't real.
    /// </summary>
    private static bool IsPlaceholderKey(string key)
    {
        var lower = key.ToLowerInvariant();

        // Common placeholder patterns
        if (lower.Contains("test") || lower.Contains("example") || lower.Contains("placeholder") ||
            lower.Contains("your_") || lower.Contains("xxx") || lower.Contains("yyy") ||
            lower.Contains("zzz") || lower.Contains("12345") || lower.Contains("abcdef") ||
            lower.Contains("dummy") || lower.Contains("fake") || lower.Contains("sample"))
            return true;

        // Repeated characters (e.g., "sk-aaaaaaaaaaaaaaaa")
        if (key.Length > 10)
        {
            var uniqueChars = key.Distinct().Count();
            if (uniqueChars < 5)
                return true;
        }

        // All same character after prefix
        if (key.Length > 8)
        {
            var suffix = key.Substring(3);
            if (suffix.Distinct().Count() == 1)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns an adaptive delay based on time of day.
    /// Faster during peak hours (9am-6pm UTC) when developers are active and more likely to commit keys.
    /// Slower at night to conserve rate limits.
    /// </summary>
    private static int GetAdaptiveDelayMs()
    {
        var hour = DateTime.UtcNow.Hour;

        // Peak hours: 9am-6pm UTC (developers actively coding)
        if (hour >= 9 && hour <= 18)
            return 3000; // 3 seconds - aggressive

        // Off-peak: 6pm-9pm UTC (some activity)
        if (hour >= 18 && hour <= 21)
            return 5000; // 5 seconds - normal

        // Night: 9pm-9am UTC (minimal activity)
        return 8000; // 8 seconds - conservative
    }

    private async Task<string?> FetchFileContentAsync(RepoReference repoRef, SearchProviderToken token)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

            // Build raw content URL from repo info
            // Format: https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}
            string? url = null;

            if (!string.IsNullOrEmpty(repoRef.RepoOwner) &&
                !string.IsNullOrEmpty(repoRef.RepoName) &&
                !string.IsNullOrEmpty(repoRef.FilePath))
            {
                var branch = repoRef.Branch ?? "main";
                url = $"https://raw.githubusercontent.com/{repoRef.RepoOwner}/{repoRef.RepoName}/{branch}/{repoRef.FilePath}";
            }

            if (string.IsNullOrEmpty(url))
                return null;

            var response = await client.GetAsync(url, _cancellationTokenSource!.Token);

            // Try 'master' if 'main' fails
            if (!response.IsSuccessStatusCode && repoRef.Branch == null)
            {
                url = $"https://raw.githubusercontent.com/{repoRef.RepoOwner}/{repoRef.RepoName}/master/{repoRef.FilePath}";
                response = await client.GetAsync(url, _cancellationTokenSource!.Token);
            }

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(_cancellationTokenSource.Token);
        }
        catch
        {
            return null;
        }
    }
}
