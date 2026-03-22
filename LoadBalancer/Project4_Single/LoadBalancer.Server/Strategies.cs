namespace LoadBalancer.Server;

public class RoundRobinStrategy : ILoadBalancingStrategy
{
    private int _index = -1;
    public string Name => "RoundRobin";

    public BackendServer? SelectServer(List<BackendServer> servers, HttpContext context)
    {
        if (servers.Count == 0) return null;
        var idx = Math.Abs(Interlocked.Increment(ref _index) % servers.Count);
        return servers[idx];
    }
}

public class LeastConnectionsStrategy : ILoadBalancingStrategy
{
    public string Name => "LeastConnections";

    public BackendServer? SelectServer(List<BackendServer> servers, HttpContext context)
        => servers.OrderBy(s => s.CurrentConnections).ThenBy(s => s.TotalRequests).FirstOrDefault();
}

public class IpHashStrategy : ILoadBalancingStrategy
{
    public string Name => "IpHash";

    public BackendServer? SelectServer(List<BackendServer> servers, HttpContext context)
    {
        if (servers.Count == 0) return null;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        uint hash = 2166136261;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(ip))
        { hash ^= b; hash *= 16777619; }
        return servers[(int)(hash % int.MaxValue) % servers.Count];
    }
}

public class WeightedRoundRobinStrategy : ILoadBalancingStrategy
{
    private int _index = -1;
    public string Name => "WeightedRoundRobin";

    public BackendServer? SelectServer(List<BackendServer> servers, HttpContext context)
    {
        if (servers.Count == 0) return null;
        var weighted = servers.SelectMany(s => Enumerable.Repeat(s, s.Weight)).ToList();
        if (weighted.Count == 0) return servers.First();
        return weighted[Math.Abs(Interlocked.Increment(ref _index) % weighted.Count)];
    }
}

public class RandomStrategy : ILoadBalancingStrategy
{
    public string Name => "Random";
    public BackendServer? SelectServer(List<BackendServer> servers, HttpContext context)
        => servers.Count == 0 ? null : servers[Random.Shared.Next(servers.Count)];
}
