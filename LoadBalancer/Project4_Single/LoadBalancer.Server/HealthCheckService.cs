namespace LoadBalancer.Server;

public class HealthCheckService : BackgroundService
{
    private readonly List<BackendServer> _servers;
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly Dictionary<string, int> _failureCounts = new();
    private const int UnhealthyThreshold = 2;
    private const int HealthCheckIntervalMs = 10000;
    private const int TimeoutMs = 3000;

    public HealthCheckService(List<BackendServer> servers, IHttpClientFactory clientFactory, ILogger<HealthCheckService> logger)
    {
        _servers = servers;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health checker started — probing {Count} servers every {Interval}s",
            _servers.Count, HealthCheckIntervalMs / 1000);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.WhenAll(_servers.Select(s => ProbeServerAsync(s, stoppingToken)));
            await Task.Delay(HealthCheckIntervalMs, stoppingToken);
        }
    }

    private async Task ProbeServerAsync(BackendServer server, CancellationToken ct)
    {
        var client = _clientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(TimeoutMs);
        try
        {
            var response = await client.GetAsync($"{server.FullAddress}/health", ct);
            server.LastHealthCheck = DateTime.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                _failureCounts[server.Id] = 0;
                if (!server.IsHealthy)
                {
                    server.IsHealthy = true;
                    _logger.LogInformation("Server {Id} recovered", server.Id);
                }
            }
            else RecordFailure(server);
        }
        catch { RecordFailure(server); }
    }

    private void RecordFailure(BackendServer server)
    {
        _failureCounts.TryGetValue(server.Id, out var count);
        _failureCounts[server.Id] = count + 1;
        if (_failureCounts[server.Id] >= UnhealthyThreshold && server.IsHealthy)
        {
            server.IsHealthy = false;
            _logger.LogWarning("Server {Id} ({Address}) marked UNHEALTHY", server.Id, server.FullAddress);
        }
    }
}
