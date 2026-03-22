namespace LoadBalancer.Server;

public class BackendServer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Weight { get; set; } = 1;
    public bool IsHealthy { get; set; } = true;
    public int TotalRequests { get; set; }
    public int FailedRequests { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

    private int _currentConnections;
    public int CurrentConnections => _currentConnections;

    public void IncrementConnections() => Interlocked.Increment(ref _currentConnections);
    public void DecrementConnections() => Interlocked.Decrement(ref _currentConnections);

    public string FullAddress => $"http://{Address}:{Port}";

    public double SuccessRate => TotalRequests == 0
        ? 100.0
        : Math.Round((TotalRequests - FailedRequests) / (double)TotalRequests * 100, 1);
}

public interface ILoadBalancingStrategy
{
    string Name { get; }
    BackendServer? SelectServer(List<BackendServer> servers, HttpContext context);
}
