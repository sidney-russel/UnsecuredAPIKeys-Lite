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
    /// Searches GitLab public snippets for exposed API keys.
    /// Similar to GitHub Gists, people paste code snippets with keys.
    /// </summary>
    public class GitLabSnippetsSearchProvider(DBContext dbContext, ILogger<GitLabSnippetsSearchProvider>? logger = null) : ISearchProvider
    {
        private const string GITLAB_API_BASE = "https://gitlab.com/api/v4";

        public string ProviderName => "GitLab Snippets";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting GitLab Snippets search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                if (!string.IsNullOrWhiteSpace(token?.Token))
                {
                    client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token.Token);
                }

                // Search public snippets
                var searchUrl = $"{GITLAB_API_BASE}/snippets?public=true&per_page=100";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("GitLab Snippets API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger?.LogInformation("No GitLab snippets found.");
                    return results;
                }

                var snippetCount = 0;
                foreach (var snippet in doc.RootElement.EnumerateArray())
                {
                    var snippetId = snippet.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
                    var title = snippet.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                    var description = snippet.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";
                    var rawUrl = snippet.TryGetProperty("raw_url", out var rawProp) ? rawProp.GetString() ?? "" : "";
                    var webUrl = snippet.TryGetProperty("web_url", out var webProp) ? webProp.GetString() ?? "" : "";
                    var author = snippet.TryGetProperty("author", out var authorProp) &&
                                 authorProp.TryGetProperty("username", out var usernameProp)
                        ? usernameProp.GetString() ?? "unknown" : "unknown";

                    // Check if title or description matches query
                    var searchText = $"{title} {description}".ToLowerInvariant();
                    if (!searchText.Contains(query.Query.ToLowerInvariant()))
                        continue;

                    // Fetch snippet content
                    if (string.IsNullOrEmpty(rawUrl)) continue;

                    try
                    {
                        var contentResponse = await client.GetAsync(rawUrl);
                        if (!contentResponse.IsSuccessStatusCode) continue;

                        var content = await contentResponse.Content.ReadAsStringAsync();

                        if (content.Length > 100_000) continue;

                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = author,
                            RepoName = $"snippet-{snippetId}",
                            FilePath = $"snippets/{snippetId}",
                            FileURL = webUrl,
                            ApiContentUrl = rawUrl,
                            Branch = "main",
                            FileSHA = snippetId.ToString(),
                            FoundUTC = DateTime.UtcNow,
                            RepoURL = webUrl,
                            RepoDescription = title,
                            FileName = $"snippet-{snippetId}.txt",
                            _cachedContent = content
                        });
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error fetching snippet {SnippetId}", snippetId);
                    }

                    snippetCount++;
                    if (snippetCount >= 50) break; // Limit per query

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during GitLab Snippets search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed GitLab Snippets search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }
    }
}
