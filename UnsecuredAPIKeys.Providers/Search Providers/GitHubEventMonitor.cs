using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Monitors GitHub events in real-time for new API key leaks.
    /// Watches for new commits, gists, and repo creation events.
    /// </summary>
    public class GitHubEventMonitor(DBContext dbContext, ILogger<GitHubEventMonitor>? logger = null) : ISearchProvider
    {
        private const string GITHUB_EVENTS_URL = "https://api.github.com/events";
        private const string GITHUB_SEARCH_URL = "https://api.github.com/search/commits";

        public string ProviderName => "GitHub Events";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Monitoring GitHub events for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                if (!string.IsNullOrWhiteSpace(token?.Token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token.Token);
                }

                // Search for recent commits containing our query
                var searchUrl = $"{GITHUB_SEARCH_URL}?q={Uri.EscapeDataString(query.Query)}+committer-date:>2025-01-01&per_page=100&order=desc";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("GitHub commit search returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                {
                    logger?.LogInformation("No recent commits found for query '{Query}'.", query.Query);
                    return results;
                }

                var commitCount = 0;
                foreach (var commit in items.EnumerateArray())
                {
                    var sha = commit.TryGetProperty("sha", out var shaProp) ? shaProp.GetString() ?? "" : "";
                    var message = commit.TryGetProperty("commit", out var commitObj) &&
                                 commitObj.TryGetProperty("message", out var msgProp)
                        ? msgProp.GetString() ?? "" : "";
                    var htmlUrl = commit.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                    var repoName = commit.TryGetProperty("repository", out var repoObj) &&
                                  repoObj.TryGetProperty("full_name", out var fnProp)
                        ? fnProp.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(sha) || string.IsNullOrEmpty(message)) continue;

                    // Check if commit message contains key patterns
                    if (!ContainsApiKeyPattern(message, query.Query)) continue;

                    results.Add(new RepoReference
                    {
                        SearchQueryId = query.Id,
                        Provider = ProviderName,
                        RepoOwner = repoName.Split('/').FirstOrDefault() ?? "",
                        RepoName = repoName,
                        FilePath = $"commit/{sha[..8]}",
                        FileURL = htmlUrl,
                        ApiContentUrl = $"https://api.github.com/repos/{repoName}/commits/{sha}",
                        Branch = "main",
                        FileSHA = sha,
                        FoundUTC = DateTime.UtcNow,
                        RepoURL = htmlUrl,
                        RepoDescription = $"Recent commit: {message[..Math.Min(100, message.Length)]}",
                        FileName = $"commit-{sha[..8]}.md",
                        _cachedContent = message
                    });

                    commitCount++;
                    if (commitCount >= 30) break;

                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }

                // Also search for new gists
                var gistSearchUrl = $"https://api.github.com/search/code?q={Uri.EscapeDataString(query.Query)}+in:file+fork:true&per_page=50";

                try
                {
                    var gistResponse = await client.GetAsync(gistSearchUrl);
                    var gistBody = await gistResponse.Content.ReadAsStringAsync();

                    if (gistResponse.IsSuccessStatusCode)
                    {
                        using var gistDoc = JsonDocument.Parse(gistBody);
                        if (gistDoc.RootElement.TryGetProperty("items", out var gistItems))
                        {
                            foreach (var item in gistItems.EnumerateArray())
                            {
                                var htmlUrl = item.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                                var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";

                                if (string.IsNullOrEmpty(htmlUrl)) continue;

                                // Only include gist results
                                if (!htmlUrl.Contains("gist.github.com", StringComparison.OrdinalIgnoreCase)) continue;

                                results.Add(new RepoReference
                                {
                                    SearchQueryId = query.Id,
                                    Provider = ProviderName,
                                    RepoOwner = "gist",
                                    RepoName = name,
                                    FilePath = name,
                                    FileURL = htmlUrl,
                                    ApiContentUrl = htmlUrl,
                                    Branch = "master",
                                    FileSHA = "",
                                    FoundUTC = DateTime.UtcNow,
                                    RepoURL = htmlUrl,
                                    RepoDescription = $"New gist: {name}",
                                    FileName = name,
                                    _cachedContent = ""
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Error searching new gists");
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error monitoring GitHub events for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed GitHub event monitoring for '{Query}'. Found {Count} potential matches.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();
            if (!lower.Contains(query.ToLowerInvariant())) return false;

            var patterns = new[] { "api_key", "token", "secret", "bearer", "sk-", "AIza", "openai", "anthropic", "replicate", "password", "credential" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
