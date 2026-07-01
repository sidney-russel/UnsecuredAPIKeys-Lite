using Microsoft.Extensions.Logging;
using Octokit;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;
using UnsecuredAPIKeys.Providers.Exceptions;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches GitHub Issues and PRs for exposed API keys.
    /// Users often paste keys when asking for help with API integration.
    /// </summary>
    public class GitHubIssuesSearchProvider(DBContext dbContext, ILogger<GitHubIssuesSearchProvider>? logger = null) : ISearchProvider
    {
        public string ProviderName => "GitHub Issues";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Token))
                throw new ArgumentNullException(nameof(token), "A valid GitHub token is required.");

            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting GitHub Issues search for query: {Query}", query.Query);

                var client = new GitHubClient(new ProductHeaderValue("UnsecuredAPIKeys-IssueScraper"))
                {
                    Credentials = new Credentials(token.Token)
                };

                // Search issues containing our query
                var searchRequest = new SearchIssuesRequest(query.Query)
                {
                    PerPage = 100,
                    Page = 1
                };

                var searchResult = await client.Search.SearchIssues(searchRequest);

                if (searchResult?.Items == null || !searchResult.Items.Any())
                {
                    logger?.LogInformation("No GitHub issues found for query '{Query}'.", query.Query);
                    return results;
                }

                query.SearchResultsCount = searchResult.TotalCount;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();

                logger?.LogDebug("Found {Count} issues for query '{Query}'.", searchResult.Items.Count, query.Query);

                foreach (var issue in searchResult.Items)
                {
                    // Check issue body for API keys
                    if (!string.IsNullOrEmpty(issue.Body))
                    {
                        var bodyContent = issue.Body;

                        // Skip issues that are too short or just questions
                        if (bodyContent.Length < 50)
                            continue;

                        // Check if the body actually contains key-like patterns
                        if (!ContainsKeyPattern(bodyContent, query.Query))
                            continue;

                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = issue.Repository?.Owner?.Login ?? "",
                            RepoName = issue.Repository?.Name ?? "",
                            FilePath = $"issues/{issue.Number}",
                            FileURL = issue.HtmlUrl,
                            ApiContentUrl = issue.Url,
                            Branch = "main",
                            FileSHA = issue.NodeId,
                            FoundUTC = DateTime.UtcNow,
                            RepoURL = issue.Repository?.HtmlUrl,
                            RepoDescription = $"Issue #{issue.Number}: {issue.Title}",
                            FileName = $"issue-{issue.Number}.md",
                            _cachedContent = bodyContent
                        });
                    }

                    // Also check comments for keys
                    try
                    {
                        var comments = await client.Issue.Comment.GetAllForIssue(
                            issue.Repository?.Owner?.Login ?? "",
                            issue.Repository?.Name ?? "",
                            issue.Number);

                        foreach (var comment in comments.Where(c => !string.IsNullOrEmpty(c.Body) && c.Body.Length > 50))
                        {
                            if (!ContainsKeyPattern(comment.Body!, query.Query))
                                continue;

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = issue.Repository?.Owner?.Login ?? "",
                                RepoName = issue.Repository?.Name ?? "",
                                FilePath = $"issues/{issue.Number}/comments/{comment.Id}",
                                FileURL = comment.HtmlUrl,
                                ApiContentUrl = comment.Url,
                                Branch = "main",
                                FileSHA = comment.NodeId,
                                FoundUTC = DateTime.UtcNow,
                                RepoURL = issue.Repository?.HtmlUrl,
                                RepoDescription = $"Comment on Issue #{issue.Number}",
                                FileName = $"comment-{comment.Id}.md",
                                _cachedContent = comment.Body
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error fetching comments for issue {IssueNumber}", issue.Number);
                    }

                    // Rate limit
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }
            }
            catch (RateLimitExceededException ex)
            {
                var delay = ex.Reset - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero && delay.TotalMinutes <= 5)
                    await Task.Delay(delay);
                else
                    throw new SearchRateLimitException(ex.Reset, $"Rate limit exceeded. Resets in {delay.TotalMinutes:F0} min.");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during GitHub Issues search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed GitHub Issues search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsKeyPattern(string content, string query)
        {
            // Check if content contains the query and key-like patterns
            var lowerContent = content.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();

            if (!lowerContent.Contains(lowerQuery))
                return false;

            // Check for key assignment patterns
            var keyPatterns = new[] { "=", ":", "api_key", "token", "secret", "bearer", "sk-", "AIza" };
            return keyPatterns.Any(p => lowerContent.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
