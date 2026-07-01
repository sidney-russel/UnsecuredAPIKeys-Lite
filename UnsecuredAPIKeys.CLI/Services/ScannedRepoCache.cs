using System.Collections.Concurrent;

namespace UnsecuredAPIKeys.CLI.Services
{
    /// <summary>
    /// In-memory cache to avoid re-scanning repos we've already looked at.
    /// Uses a concurrent dictionary for thread safety with parallel scraping.
    /// </summary>
    public class ScannedRepoCache
    {
        private readonly ConcurrentDictionary<string, DateTime> _cache = new();
        private readonly TimeSpan _cacheExpiry;
        private readonly int _maxSize;

        /// <summary>
        /// Creates a new ScannedRepoCache.
        /// </summary>
        /// <param name="cacheExpiry">How long to remember scanned repos (default: 24 hours)</param>
        /// <param name="maxSize">Max number of repos to cache (default: 10000)</param>
        public ScannedRepoCache(TimeSpan? cacheExpiry = null, int maxSize = 10000)
        {
            _cacheExpiry = cacheExpiry ?? TimeSpan.FromHours(24);
            _maxSize = maxSize;
        }

        /// <summary>
        /// Checks if a repo has been scanned recently.
        /// </summary>
        public bool IsScanned(string repoOwner, string repoName)
        {
            var key = $"{repoOwner}/{repoName}".ToLowerInvariant();

            if (_cache.TryGetValue(key, out var scannedAt))
            {
                if (DateTime.UtcNow - scannedAt < _cacheExpiry)
                    return true;

                // Expired, remove it
                _cache.TryRemove(key, out _);
            }

            return false;
        }

        /// <summary>
        /// Marks a repo as scanned.
        /// </summary>
        public void MarkScanned(string repoOwner, string repoName)
        {
            var key = $"{repoOwner}/{repoName}".ToLowerInvariant();

            // Evict oldest entries if cache is full
            if (_cache.Count >= _maxSize)
            {
                var oldest = _cache
                    .OrderBy(kvp => kvp.Value)
                    .Take(_cache.Count / 10) // Remove oldest 10%
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var k in oldest)
                {
                    _cache.TryRemove(k, out _);
                }
            }

            _cache[key] = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets the number of cached repos.
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Clears the cache.
        /// </summary>
        public void Clear() => _cache.Clear();
    }
}
