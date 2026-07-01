using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers;

/// <summary>
/// Implements the ISearchProvider interface for searching code on SourceGraph.
/// SourceGraph provides a free public code search API.
/// </summary>
public class SourceGraphSearchProvider(ILogger<SourceGraphSearchProvider>? logger = null) : ISearchProvider
{
    private const string SourceGraphApiUrl = "https://sourcegraph.com/.api/search/stream";
    private static readonly HttpClient SharedClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public string ProviderName => "SourceGraph";

    public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
    {
        if (query == null || string.IsNullOrWhiteSpace(query.Query))
        {
            logger?.LogError("Search query is missing or invalid.");
            throw new ArgumentNullException(nameof(query), "A valid search query is required.");
        }

        var results = new List<RepoReference>();

        try
        {
            logger?.LogInformation("Starting SourceGraph search for query: {Query}", query.Query);

            // SourceGraph search API - searches across all public repositories
            var searchQuery = Uri.EscapeDataString(query.Query);
            var url = $"{SourceGraphApiUrl}?q={searchQuery}&display=50";

            var response = await SharedClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                logger?.LogWarning("SourceGraph API returned status {StatusCode}", response.StatusCode);
                return results;
            }

            var json = await response.Content.ReadAsStringAsync();
            var searchResults = JsonSerializer.Deserialize<JsonElement>(json);

            if (searchResults.TryGetProperty("results", out var resultsArray))
            {
                foreach (var result in resultsArray.EnumerateArray())
                {
                    if (!result.TryGetProperty("repository", out var repoElement))
                        continue;

                    var repoName = repoElement.GetString() ?? "";
                    if (string.IsNullOrEmpty(repoName))
                        continue;

                    // Parse owner/repo format
                    var parts = repoName.Split('/', 2);
                    if (parts.Length < 2) continue;

                    var repoOwner = parts[0];
                    var repoNameOnly = parts[1];

                    // Get file matches if available
                    if (result.TryGetProperty("fileMatches", out var fileMatches))
                    {
                        foreach (var fileMatch in fileMatches.EnumerateArray())
                        {
                            var filePath = fileMatch.TryGetProperty("path", out var pathEl)
                                ? pathEl.GetString() : null;

                            if (string.IsNullOrEmpty(filePath))
                                continue;

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = repoOwner,
                                RepoName = repoNameOnly,
                                FilePath = filePath,
                                FileURL = $"https://github.com/{repoOwner}/{repoNameOnly}/blob/main/{filePath}",
                                RepoURL = $"https://github.com/{repoOwner}/{repoNameOnly}",
                                Branch = "main",
                                FoundUTC = DateTime.UtcNow
                            });
                        }
                    }
                    else
                    {
                        // No file-level matches, add repo-level reference
                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = repoOwner,
                            RepoName = repoNameOnly,
                            FileURL = $"https://github.com/{repoOwner}/{repoNameOnly}",
                            RepoURL = $"https://github.com/{repoOwner}/{repoNameOnly}",
                            Branch = "main",
                            FoundUTC = DateTime.UtcNow
                        });
                    }
                }
            }

            logger?.LogInformation("SourceGraph search completed. Found {Count} references.", results.Count);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogError(ex, "HTTP error during SourceGraph search for query: {Query}", query.Query);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "An unexpected error occurred during SourceGraph search for query: {Query}", query.Query);
        }

        return results;
    }
}
