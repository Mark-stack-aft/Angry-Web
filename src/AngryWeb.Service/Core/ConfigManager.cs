using System.Text.Json;

namespace AngryWeb.Service.Core;

public class ConfigManager
{
    private readonly ILogger<ConfigManager> _logger;
    private readonly HttpClient _httpClient = new();
    private FilterConfig _config = new();
    private readonly string _localConfigPath;

    public FilterConfig CurrentConfig => _config;

    public event Action? ConfigUpdated;

    public ConfigManager(ILogger<ConfigManager> logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dir = Path.Combine(appData, "AngryWeb");
        Directory.CreateDirectory(dir);
        _localConfigPath = Path.Combine(dir, "config.json");

        LoadLocalConfig();
    }

    public void LoadLocalConfig()
    {
        try
        {
            if (File.Exists(_localConfigPath))
            {
                var json = File.ReadAllText(_localConfigPath);
                var loaded = JsonSerializer.Deserialize<FilterConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (loaded != null)
                {
                    _config = loaded;
                    _logger.LogInformation("Loaded local configuration from {Path}", _localConfigPath);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load local config, using defaults.");
        }

        // Initialize default rules
        _config = new FilterConfig
        {
            Rules = new List<RuleDefinition>
            {
                new() { Pattern = "youtube.com/shorts/*", Description = "YouTube Shorts 차단" },
                new() { Pattern = "*.tiktok.com/*", Description = "틱톡 전면 차단" },
                new() { Pattern = "instagram.com/reels/*", Description = "인스타그램 릴스 차단" },
                new() { Pattern = "*chzzk.naver.com*", Description = "치지직 방송 차단" },
                new() { Pattern = "reddit.com/r/all/*", Description = "레딧 All 차단" }
            }
        };
        SaveLocalConfig();
    }

    public void SaveLocalConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_localConfigPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save local config.");
        }
    }

    public async Task SyncWithGitHubAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.GitHubRulesUrl))
            return;

        try
        {
            _logger.LogInformation("Syncing rules from GitHub: {Url}", _config.GitHubRulesUrl);
            var response = await _httpClient.GetAsync(_config.GitHubRulesUrl, ct);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                var remoteConfig = JsonSerializer.Deserialize<FilterConfig>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (remoteConfig != null && remoteConfig.Rules.Count > 0)
                {
                    // Merge remote rules while retaining local port & password if not provided
                    _config.Rules = remoteConfig.Rules;
                    if (remoteConfig.BlockPage != null)
                    {
                        _config.BlockPage = remoteConfig.BlockPage;
                    }
                    SaveLocalConfig();
                    ConfigUpdated?.Invoke();
                    _logger.LogInformation("Successfully synced {Count} rules from GitHub.", _config.Rules.Count);
                }
            }
            else
            {
                _logger.LogWarning("GitHub sync returned status code: {Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("GitHub sync skipped: {Message}", ex.Message);
        }
    }
}
