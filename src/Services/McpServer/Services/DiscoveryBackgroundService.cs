using McpApis.McpServer.Services.Contracts;

namespace McpApis.McpServer.Services;

/// <summary>
/// Re-runs discovery periodically (Discovery:RescanSeconds, default 60; 0 disables
/// the timer) and whenever a rescan is requested via the dashboard/orchestrator.
/// The initial blocking scan happens in Program.cs before the server accepts traffic.
/// </summary>
public class DiscoveryBackgroundService : BackgroundService
{
    private readonly DiscoveryOrchestrator _orchestrator;
    private readonly IConfiguration _config;
    private readonly ILogger<DiscoveryBackgroundService> _logger;

    public DiscoveryBackgroundService(
        DiscoveryOrchestrator orchestrator,
        IConfiguration config,
        ILogger<DiscoveryBackgroundService> logger)
    {
        _orchestrator = orchestrator;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rescanSeconds = int.TryParse(_config["Discovery:RescanSeconds"], out var value) ? value : 60;
        var interval = rescanSeconds <= 0
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromSeconds(rescanSeconds);

        _logger.LogInformation(
            "Discovery background service started (interval: {Interval}).",
            rescanSeconds <= 0 ? "manual only" : $"{rescanSeconds}s");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var timer = Task.Delay(interval, stoppingToken);
                var signal = _orchestrator.RescanSignal.WaitToReadAsync(stoppingToken).AsTask();
                await Task.WhenAny(timer, signal);
                stoppingToken.ThrowIfCancellationRequested();

                while (_orchestrator.RescanSignal.TryRead(out _)) { }

                await _orchestrator.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Discovery scan failed; will retry on the next cycle.");
            }
        }
    }
}
