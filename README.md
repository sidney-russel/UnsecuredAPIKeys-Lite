# UnsecuredAPIKeys Lite

An advanced API key discovery tool that searches across **12 different sources** to find exposed API keys on GitHub, GitLab, Hugging Face, Reddit, Stack Overflow, Pastebin, and more.

> **Disclaimer**: This tool is for educational and security research purposes only. Always obtain proper authorization before testing API keys. Respect all terms of service.

## Features

### 12 Search Sources

| Source | What It Searches |
|--------|------------------|
| GitHub Code | Public repositories for hardcoded keys |
| GitHub Gists | Public pastes with API configurations |
| GitHub Issues/PRs | Users pasting keys when asking for help |
| GitLab | Public GitLab repositories |
| GitLab Snippets | Code snippets with embedded keys |
| Stack Overflow | Code snippets in questions/answers |
| Pastebin | Public pastes with configs |
| Hugging Face | AI model deployments with API keys |
| Reddit | Posts in r/ChatGPT, r/LocalLLaMA, r/selfhosted |
| Replicate | Model deployment configurations |
| GitHub Events | Real-time commit monitoring |
| Leak Databases | Known public API key leaks |

### Smart Query Optimization

- **60+ targeted queries** using GitHub search qualifiers
- **Freshness filtering** — searches recent commits (keys more likely valid)
- **File type targeting** — prioritizes `.env`, `config.*`, `Dockerfile`
- **Priority queue** — high-value queries run first

### Speed Optimizations

- **5x parallel scraping** — processes multiple files simultaneously
- **5x parallel verification** — validates multiple keys at once
- **Multi-token rotation** — add multiple GitHub tokens for higher rate limits
- **Repo caching** — skips repos scanned in last 24 hours
- **Adaptive timing** — faster during peak hours, slower at night

### Quality Improvements

- **Smart deduplication** — skips docs, tests, examples, binaries
- **Placeholder detection** — filters test/dummy keys automatically
- **Re-verification** — re-checks valid keys every 30 minutes
- **Database indexes** — faster queries on large datasets

## Prerequisites

- .NET 10 SDK or later
- GitHub Personal Access Token (with `public_repo` scope)
- Optional: GitHub tokens for additional sources

## Installation

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/UnsecuredAPIKeys.Lite.git
cd UnsecuredAPIKeys.Lite

# Build the project
dotnet build

# Run the tool
cd UnsecuredAPIKeys.CLI
dotnet run
```

## First-Time Setup

1. **Launch the tool**:
   ```bash
   cd UnsecuredAPIKeys.CLI
   dotnet run
   ```

2. **Configure your GitHub token**:
   - Select **"Configure Settings"** from the main menu
   - Select **"Set GitHub Token"**
   - Enter your GitHub Personal Access Token
   - The token will be encrypted and stored in the database

3. **Create a GitHub token** (if you don't have one):
   - Go to https://github.com/settings/tokens
   - Click "Generate new token (classic)"
   - Select `public_repo` scope
   - Copy the token

4. **Add multiple tokens** (recommended for speed):
   - Each token gives 30 searches/minute
   - 3 tokens = 90 searches/minute
   - Run "Configure Settings" > "Set GitHub Token" multiple times

## Usage

### Main Menu

```
1. Start Scraper (search for keys)
2. Start Verifier (validate found keys)
3. View Status (statistics)
4. Configure Settings
5. Export Keys
6. Exit
```

### Step 1: Scrape for Keys

1. Select **"Start Scraper"**
2. The tool will search all 12 sources automatically
3. Watch as it finds potential API keys
4. Press `Ctrl+C` to stop scraping

### Step 2: Verify Keys

1. Select **"Start Verifier"**
2. The tool will validate found keys against provider APIs
3. Valid keys are marked and ready to use
4. Press `Ctrl+C` to stop verification

### Step 3: View Status

1. Select **"View Status"**
2. See statistics:
   - Total keys found
   - Valid/Invalid/Unverified counts
   - Keys by provider (OpenAI, Anthropic, Google)

### Step 4: Export Keys

1. Select **"Export Keys"**
2. Choose format (JSON or CSV)
3. Choose to export all or valid keys only
4. File is saved to current directory

## Configuration Options

### Multi-Token Setup

```bash
# Add first token
Configure Settings > Set GitHub Token > Enter token

# Add second token
Configure Settings > Set GitHub Token > Enter another token

# Add third token
Configure Settings > Set GitHub Token > Enter third token
```

### Rate Limits

| Tokens | Searches/Minute | Searches/Hour |
|--------|-----------------|---------------|
| 1 | 30 | 1,800 |
| 2 | 60 | 3,600 |
| 3 | 90 | 5,400 |

### Search Queries

The tool uses 60+ optimized queries including:

```
# High-value queries (run first)
sk-proj- filename:.env
OPENAI_API_KEY= filename:config
ANTHROPIC_API_KEY= language:python
AIzaSy filename:.env

# Freshness-filtered queries
OPENAI_API_KEY created:>=2025-06-01
sk-ant-api01 created:>=2025-06-01

# File-type targeted queries
OPENAI_API_KEY filename:Dockerfile
ANTHROPIC_API_KEY filename:.github/workflows
```

## Database

The tool uses SQLite for local storage:

- **Location**: `unsecuredapikeys.db` (in the CLI directory)
- **Automatic backup**: Database is created automatically
- **Reset**: Use "Configure Settings" > "Reset Database" to start fresh

### Database Schema

- **APIKeys**: Stores found API keys with status
- **RepoReferences**: Tracks where each key was found
- **SearchQueries**: Manages search queries and timing
- **SearchProviderTokens**: Stores encrypted GitHub tokens

## Troubleshooting

### "No GitHub token configured"

```
Configure Settings > Set GitHub Token > Enter your token
```

### "Rate limit exceeded"

- Add more GitHub tokens (each gives 30 searches/min)
- Wait for rate limit to reset (usually 1 hour)
- Reduce search frequency in Settings

### "No queries due for search"

- Wait a few seconds — queries cycle automatically
- Delete database to reset query timing:
  ```bash
  rm unsecuredapikeys.db
  dotnet run
  ```

### "0 keys found"

This is normal! Most searches return test/placeholder keys. The tool filters these automatically. Real working keys are rare.

## Performance Tips

1. **Add multiple tokens** — 3 tokens = 3x faster
2. **Run during peak hours** — 9am-6pm UTC for more activity
3. **Let it run overnight** — more time = more results
4. **Delete database periodically** — resets query timing

## Project Structure

```
UnsecuredAPIKeys.Lite/
├── UnsecuredAPIKeys.CLI/          # Main application
│   ├── Program.cs                 # Entry point & menu
│   ├── Constants.cs               # Configuration constants
│   └── Services/
│       ├── ScraperService.cs      # Main scraping logic
│       ├── VerifierService.cs     # Key validation
│       ├── DatabaseService.cs     # Database operations
│       ├── TokenEncryption.cs     # Token encryption
│       ├── TokenRotationService.cs # Multi-token rotation
│       └── ScannedRepoCache.cs    # Repo caching
├── UnsecuredAPIKeys.Data/         # Data models & database
│   ├── DBContext.cs               # Entity Framework context
│   ├── Models/                    # Data models
│   ├── Migrations/                # Database migrations
│   └── Common/                    # Enums & shared types
├── UnsecuredAPIKeys.Providers/    # Search & verification providers
│   ├── Search Providers/          # 12 search sources
│   ├── AI Providers/              # API key validation
│   ├── _Interfaces/               # Provider contracts
│   └── _Base/                     # Base implementations
└── UnsecuredAPIKeys.Tests/        # Unit tests
```

## Technical Details

### Search Pipeline

```
Query Selection (Priority Queue)
    ↓
Source Search (12 sources, parallel)
    ↓
Content Fetching (5 concurrent)
    ↓
Regex Matching (compiled patterns)
    ↓
Deduplication & Filtering
    ↓
Database Storage
```

### Verification Pipeline

```
Unverified Keys (recent first)
    ↓
Provider Matching (API type detection)
    ↓
API Validation (with retry logic)
    ↓
Status Update (Valid/Invalid/Error)
```

### Rate Limit Handling

- **GitHub**: 30 searches/min per token (authenticated)
- **GitLab**: 20 requests/min
- **Stack Overflow**: 30 requests/day (unauthenticated)
- **Pastebin**: 1 request/second
- **Hugging Face**: 100 requests/hour

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Run tests: `dotnet test`
5. Submit a pull request

## License

MIT License - see [LICENSE](LICENSE) for details

## Acknowledgments

- GitHub API for code search
- GitLab API for repository search
- Stack Overflow API for Q&A search
- Hugging Face for AI model search

## Support

- **Issues**: Report bugs on GitHub
- **Discussions**: Ask questions in GitHub Discussions
- **Wiki**: Check the wiki for advanced usage

---

**Remember**: This tool is for educational purposes. Always use API keys responsibly and ethically.
