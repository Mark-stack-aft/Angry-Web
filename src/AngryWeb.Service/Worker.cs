using AngryWeb.Service.Core;

namespace AngryWeb.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ConfigManager _configManager;
    private readonly PatternMatcher _patternMatcher;
    private readonly BlockPageServer _blockPageServer;
    private readonly ProxyFilterEngine _proxyFilterEngine;

    public Worker(
        ILogger<Worker> logger,
        ConfigManager configManager,
        PatternMatcher patternMatcher,
        BlockPageServer blockPageServer,
        ProxyFilterEngine proxyFilterEngine)
    {
        _logger = logger;
        _configManager = configManager;
        _patternMatcher = patternMatcher;
        _blockPageServer = blockPageServer;
        _proxyFilterEngine = proxyFilterEngine;

        _configManager.ConfigUpdated += OnConfigUpdated;
    }

    private void OnConfigUpdated()
    {
        _logger.LogInformation("Configuration updated, reloading pattern rules...");
        _patternMatcher.LoadRules(_configManager.CurrentConfig.Rules);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Angry Web Service is starting at: {time}", DateTimeOffset.Now);

        // 1. Initial rule compile
        _patternMatcher.LoadRules(_configManager.CurrentConfig.Rules);

        // 2. Start Block Page server
        _blockPageServer.Start(_configManager.CurrentConfig.BlockPagePort);

        // 3. Start Proxy Filter Engine
        _proxyFilterEngine.Start();

        // 4. Initial GitHub Sync
        await _configManager.SyncWithGitHubAsync(stoppingToken);

        // 5. Periodic GitHub Sync loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var interval = Math.Max(1, _configManager.CurrentConfig.SyncIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(interval), stoppingToken);
                await _configManager.SyncWithGitHubAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in periodic sync loop.");
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Angry Web Service is stopping...");
        _proxyFilterEngine.Stop();
        _blockPageServer.Stop();
        return base.StopAsync(cancellationToken);
    }
}
