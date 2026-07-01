using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Common;
using UnsecuredAPIKeys.Data.Models;

namespace UnsecuredAPIKeys.CLI.Services
{
    /// <summary>
    /// Manages multiple GitHub tokens with rotation for higher rate limits.
    /// Each token gives 30 code searches/min. Multiple tokens multiply throughput.
    /// </summary>
    public class TokenRotationService
    {
        private readonly DBContext _dbContext;
        private readonly ILogger? _logger;
        private readonly List<SearchProviderToken> _tokens = [];
        private int _currentTokenIndex;
        private readonly object _lock = new();

        public TokenRotationService(DBContext dbContext, ILogger? logger = null)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Loads all enabled tokens for the given provider.
        /// </summary>
        public async Task LoadTokensAsync(SearchProviderEnum provider)
        {
            var tokens = await _dbContext.SearchProviderTokens
                .Where(t => t.IsEnabled && t.SearchProvider == provider)
                .OrderBy(t => t.Id)
                .ToListAsync();

            _tokens.Clear();
            _tokens.AddRange(tokens);

            // Decrypt tokens
            foreach (var token in _tokens)
            {
                if (TokenEncryption.IsEncrypted(token.Token))
                {
                    token.Token = TokenEncryption.Decrypt(token.Token);
                }
            }

            if (_logger != null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Loaded {Count} {Provider} tokens for rotation.", _tokens.Count, provider.ToString());
            }
        }

        /// <summary>
        /// Gets the next available token using round-robin rotation.
        /// Returns null if no tokens are loaded.
        /// </summary>
        public SearchProviderToken? GetNextToken()
        {
            lock (_lock)
            {
                if (_tokens.Count == 0)
                    return null;

                var token = _tokens[_currentTokenIndex % _tokens.Count];
                _currentTokenIndex++;

                // Update last used time
                token.LastUsedUTC = DateTime.UtcNow;

                return token;
            }
        }

        /// <summary>
        /// Gets all loaded tokens.
        /// </summary>
        public IReadOnlyList<SearchProviderToken> GetAllTokens() => _tokens.AsReadOnly();

        /// <summary>
        /// Gets the number of loaded tokens.
        /// </summary>
        public int TokenCount => _tokens.Count;

        /// <summary>
        /// Saves usage stats to database.
        /// </summary>
        public async Task SaveUsageStatsAsync()
        {
            foreach (var token in _tokens)
            {
                var dbToken = await _dbContext.SearchProviderTokens
                    .FirstOrDefaultAsync(t => t.Id == token.Id);

                if (dbToken != null)
                {
                    dbToken.LastUsedUTC = token.LastUsedUTC;
                }
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
