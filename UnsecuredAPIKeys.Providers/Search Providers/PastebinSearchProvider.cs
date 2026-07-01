using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;
using UnsecuredAPIKeys.Providers.Exceptions;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches Pastebin for exposed API keys.
    /// Pastebin has a public API and many people paste configs with API keys.
    /// </summary>
    public class PastebinSearchProvider(DBContext dbContext, ILogger<PastebinSearchProvider>? logger = null) : ISearchProvider
    {
        public string ProviderName => "Pastebin";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting Pastebin search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                // Pastebin has a public search API via their website
                // We can search for pastes containing our query
                var searchUrl = $"https://pastebin.com/api/search.php";

                // Pastebin API requires a developer key for search
                // Without one, we can scrape the search page
                var searchPageUrl = $"https://pastebin.com/search?q={Uri.EscapeDataString(query.Query)}&time=1d";

                var response = await client.GetAsync(searchPageUrl);
                var html = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Pastebin returned {StatusCode} for search.", response.StatusCode);
                    return results;
                }

                // Parse paste IDs from the search results HTML
                var pasteIds = ExtractPasteIds(html);

                if (pasteIds.Count == 0)
                {
                    logger?.LogInformation("No Pastebin results found for query '{Query}'.", query.Query);
                    return results;
                }

                query.SearchResultsCount = pasteIds.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();

                logger?.LogDebug("Found {Count} pastes for query '{Query}'.", pasteIds.Count, query.Query);

                // Fetch each paste content
                foreach (var pasteId in pasteIds.Take(50)) // Limit to 50 pastes per query
                {
                    try
                    {
                        var pasteUrl = $"https://pastebin.com/raw/{pasteId}";
                        var pasteResponse = await client.GetAsync(pasteUrl);

                        if (!pasteResponse.IsSuccessStatusCode)
                            continue;

                        var content = await pasteResponse.Content.ReadAsStringAsync();

                        // Skip large pastes
                        if (content.Length > 100_000)
                            continue;

                        // Check if paste actually contains our search terms
                        if (!content.Contains(query.Query, StringComparison.OrdinalIgnoreCase))
                            continue;

                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = "pastebin",
                            RepoName = pasteId,
                            FilePath = $"{pasteId}.txt",
                            FileURL = $"https://pastebin.com/{pasteId}",
                            ApiContentUrl = pasteUrl,
                            Branch = "master",
                            FileSHA = pasteId,
                            FoundUTC = DateTime.UtcNow,
                            RepoURL = $"https://pastebin.com/{pasteId}",
                            RepoDescription = $"Pastebin paste {pasteId}",
                            FileName = $"{pasteId}.txt",
                            _cachedContent = content
                        });
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error fetching paste {PasteId}", pasteId);
                    }

                    // Rate limit: 1 request per second for unauthenticated
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during Pastebin search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Pastebin search for '{Query}'. Found {Count} paste references.", query.Query, results.Count);
            return results;
        }

        private static List<string> ExtractPasteIds(string html)
        {
            var ids = new List<string>();

            // Extract paste IDs from search results
            // Pastebin uses format: /paste_id in links
            var regex = new System.Text.RegularExpressions.Regex(@"/([a-zA-Z0-9]{8,})\b");
            var matches = regex.Matches(html);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var id = match.Groups[1].Value;
                // Filter out common non-paste IDs
                if (id.Length >= 8 && id != "search" && id != "archive" && id != "tools")
                {
                    ids.Add(id);
                }
            }

            return ids.Distinct().ToList();
        }
    }
}
