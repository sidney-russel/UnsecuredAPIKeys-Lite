using Microsoft.Extensions.Logging;
using Octokit;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;
using UnsecuredAPIKeys.Providers.Exceptions;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Implements the ISearchProvider interface for searching code on GitHub.
    /// </summary>
    public class GitHubSearchProvider(DBContext dbContext, ILogger<GitHubSearchProvider>? logger = null) : ISearchProvider
    {
        /// <inheritdoc />
        public string ProviderName => "GitHub";

        /// <inheritdoc />
        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Token))
            {
                logger?.LogError("GitHub token is missing or invalid."); // Use _logger field
                throw new ArgumentNullException(nameof(token), "A valid GitHub token is required.");
            }

            if (query == null || string.IsNullOrWhiteSpace(query.Query))
            {
                logger?.LogError("Search query is missing or invalid."); // Use _logger field
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");
            }

            var client = new GitHubClient(new ProductHeaderValue("UnsecuredAPIKeys-Scraper"))
            {
                Credentials = new Credentials(token.Token)
            };

            var results = new List<RepoReference>();
            int page = 1;
            const int perPage = 100; // Max allowed by GitHub API

            try
            {
                logger?.LogInformation("Starting GitHub search for query: {Query}", query.Query); // Use _logger field

                while (true) // Loop to handle pagination
                {
                    var request = new SearchCodeRequest(query.Query)
                    {
                        // Consider adding filters like language, user, repo if needed
                        Page = page,
                        PerPage = perPage
                        // Order = SortDirection.Descending
                    };

                    SearchCodeResult searchResult;
                    try
                    {
                        searchResult = await client.Search.SearchCode(request);

                        // Track rate limit from response headers
                        if (page == 1)
                        {
                            query.SearchResultsCount = searchResult.TotalCount;
                            dbContext.SearchQueries.Update(query);
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    catch (RateLimitExceededException ex)
                    {
                        logger?.LogWarning("GitHub API rate limit exceeded. Waiting until {ResetTime}.", ex.Reset.ToString("o"));

                        // Wait until the rate limit resets
                        var delay = ex.Reset - DateTimeOffset.UtcNow;
                        if (delay > TimeSpan.Zero)
                        {
                            if (delay.TotalMinutes > 5)
                            {
                                // Rate limit reset is far away — throw so caller can try other providers
                                throw new SearchRateLimitException(ex.Reset,
                                    $"GitHub rate limit exceeded. Resets in {delay.TotalMinutes:F0} minutes.");
                            }

                            // Wait with a small buffer
                            await Task.Delay(delay.Add(TimeSpan.FromSeconds(5)));
                        }
                        continue; // Retry the same page
                    }
                    catch (ApiException apiEx)
                    {
                        logger?.LogError(apiEx, "GitHub API error during search on page {Page}. Status: {StatusCode}", page, apiEx.StatusCode); // Use _logger field
                        // Decide how to handle API errors (e.g., stop, retry after delay)
                        break; // Stop searching on API error for now
                    }

                    if (searchResult?.Items == null || !searchResult.Items.Any())
                    {
                        logger?.LogInformation("No more results found for query '{Query}' on page {Page}.", query.Query, page); // Use _logger field
                        break; // No more results
                    }

                    logger?.LogDebug("Found {Count} results on page {Page} for query '{Query}'.", searchResult.Items.Count, page, query.Query); // Use _logger field

                    foreach (var item in searchResult.Items)
                    {
                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = item.Repository?.Owner?.Login, // Corrected field name
                            RepoName = item.Repository?.Name, // Corrected field name
                            FilePath = item.Path,
                            FileURL = item.HtmlUrl, // HTML URL for viewing in browser
                            ApiContentUrl = item.Url, // API URL for fetching content
                            Branch = item.Repository?.DefaultBranch, // Assuming default branch, might need refinement
                            FileSHA = item.Sha, // Corrected field name (SHA of the file blob)
                            FoundUTC = DateTime.UtcNow, // Corrected field name (Record when this specific reference was found)
                            RepoURL = item.Repository?.HtmlUrl,
                            RepoDescription = item.Repository?.Description,
                            FileName = item.Name
                        });
                    }

                    // Basic check to prevent infinite loops if API behaves unexpectedly
                    if (searchResult.Items.Count < perPage || results.Count >= 1000) // GitHub limits code search results to 1000
                    {
                        logger?.LogInformation("Finished GitHub search for query '{Query}'. Total results processed: {TotalCount}. Reached page limit or result cap.", query.Query, results.Count); // Use _logger field
                        break;
                    }

                    page++; // Move to the next page
                    await Task.Delay(TimeSpan.FromSeconds(2)); // Add a small delay to be polite to the API
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "An unexpected error occurred during GitHub search for query: {Query}", query.Query); // Use _logger field
            }

            logger?.LogInformation("Completed GitHub search for query '{Query}'. Found {Count} potential references.", query.Query, results.Count); // Use _logger field
            return results;
        }
    }
}
