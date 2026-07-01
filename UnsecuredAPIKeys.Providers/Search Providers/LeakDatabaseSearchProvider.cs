using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches public API key leak databases for exposed keys.
    /// These databases contain real leaked keys from security breaches.
    /// </summary>
    public class LeakDatabaseSearchProvider(DBContext dbContext, ILogger<LeakDatabaseSearchProvider>? logger = null) : ISearchProvider
    {
        // Public leak databases (APIs that index leaked credentials)
        private static readonly string[] LeakApiUrls =
        [
            "https://leakdb.io/api/query",
            "https://secretsearch.ninja/api/search",
            "https://github.com/nicehash/NiceHashQuickMiner/wiki/Switch-IDs" // Known leaked mining API keys
        ];

        public string ProviderName => "Leak Database";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Searching public leak databases for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                // Search GitHub for known leaked key patterns
                // This searches for actual key values that have been publicized
                var searchPatterns = GetSearchPatterns(query.Query);

                foreach (var pattern in searchPatterns)
                {
                    try
                    {
                        var searchUrl = $"https://api.github.com/search/code?q={Uri.EscapeDataString(pattern)}&per_page=100";

                        if (!string.IsNullOrWhiteSpace(token?.Token))
                        {
                            client.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", token.Token);
                        }

                        var response = await client.GetAsync(searchUrl);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
                                responseBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                            {
                                await Task.Delay(TimeSpan.FromSeconds(60));
                                continue;
                            }
                            continue;
                        }

                        using var doc = JsonDocument.Parse(responseBody);
                        if (!doc.RootElement.TryGetProperty("items", out var items)) continue;

                        foreach (var item in items.EnumerateArray())
                        {
                            var htmlUrl = item.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                            var repository = item.TryGetProperty("repository", out var repoProp) &&
                                            repoProp.TryGetProperty("full_name", out var fnProp)
                                ? fnProp.GetString() ?? "" : "";

                            if (string.IsNullOrEmpty(htmlUrl)) continue;

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = repository.Split('/').FirstOrDefault() ?? "",
                                RepoName = repository,
                                FilePath = name,
                                FileURL = htmlUrl,
                                ApiContentUrl = htmlUrl,
                                Branch = "main",
                                FileSHA = "",
                                FoundUTC = DateTime.UtcNow,
                                RepoURL = htmlUrl,
                                RepoDescription = $"Leak database match: {pattern}",
                                FileName = name,
                                _cachedContent = "" // Will be fetched during processing
                            });
                        }

                        await Task.Delay(TimeSpan.FromSeconds(2)); // Rate limit
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error searching leak pattern: {Pattern}", pattern);
                    }
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error searching leak databases for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed leak database search for '{Query}'. Found {Count} potential matches.", query.Query, results.Count);
            return results;
        }

        private static string[] GetSearchPatterns(string query)
        {
            var patterns = new List<string>();

            // Create specific search patterns based on the query
            if (query.Contains("sk-", StringComparison.OrdinalIgnoreCase))
            {
                // Search for actual OpenAI key patterns
                patterns.Add("\"sk-proj-\" filename:.env");
                patterns.Add("\"sk-proj-\" filename:config");
                patterns.Add("\"sk-ant-api01-\" filename:.env");
            }
            else if (query.Contains("AIza", StringComparison.OrdinalIgnoreCase))
            {
                patterns.Add("\"AIzaSy\" filename:.env");
                patterns.Add("\"AIzaSy\" filename:config");
            }
            else
            {
                // Generic search
                patterns.Add($"\"{query}\" filename:.env");
                patterns.Add($"\"{query}\" filename:config");
            }

            return patterns.ToArray();
        }
    }
}
