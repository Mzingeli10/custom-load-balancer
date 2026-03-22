using LoadBalancer.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

builder.Services.AddSingleton<List<BackendServer>>(_ => new List<BackendServer>
{
    new() { Address = "localhost", Port = 5001, Weight = 2 },
    new() { Address = "localhost", Port = 5002, Weight = 1 },
    new() { Address = "localhost", Port = 5003, Weight = 1 },
});

// Swap strategy by commenting in/out:
builder.Services.AddSingleton<ILoadBalancingStrategy>(new RoundRobinStrategy());
// builder.Services.AddSingleton<ILoadBalancingStrategy>(new LeastConnectionsStrategy());
// builder.Services.AddSingleton<ILoadBalancingStrategy>(new IpHashStrategy());
// builder.Services.AddSingleton<ILoadBalancingStrategy>(new WeightedRoundRobinStrategy());

builder.Services.AddSingleton(new RateLimiter(maxTokens: 20, refillRatePerSecond: 5));
builder.Services.AddSingleton(new LruCache<string, string>(capacity: 500));
builder.Services.AddHostedService<HealthCheckService>();

builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

var servers     = app.Services.GetRequiredService<List<BackendServer>>();
var strategy    = app.Services.GetRequiredService<ILoadBalancingStrategy>();
var httpFac     = app.Services.GetRequiredService<IHttpClientFactory>();
var rateLimiter = app.Services.GetRequiredService<RateLimiter>();
var cache       = app.Services.GetRequiredService<LruCache<string, string>>();

// ── Admin endpoints ────────────────────────────────────────────

app.MapGet("/lb/health", () => Results.Ok(new
{
    status = "healthy",
    strategy = strategy.Name,
    timestamp = DateTime.UtcNow
}));

app.MapGet("/lb/stats", () => Results.Ok(new
{
    strategy = strategy.Name,
    cacheSize = cache.Count,
    servers = servers.Select(s => new
    {
        s.Id, s.FullAddress, s.IsHealthy,
        s.CurrentConnections, s.TotalRequests,
        s.FailedRequests, s.SuccessRate, s.Weight,
        s.LastHealthCheck
    })
}));

app.MapGet("/lb/metrics", () =>
{
    var lines = new List<string>();
    lines.Add("# HELP lb_backend_total_requests Total requests per backend");
    lines.Add("# TYPE lb_backend_total_requests counter");
    foreach (var s in servers)
        lines.Add($"lb_backend_total_requests{{id=\"{s.Id}\"}} {s.TotalRequests}");
    lines.Add("# HELP lb_backend_healthy Backend health");
    lines.Add("# TYPE lb_backend_healthy gauge");
    foreach (var s in servers)
        lines.Add($"lb_backend_healthy{{id=\"{s.Id}\"}} {(s.IsHealthy ? 1 : 0)}");
    lines.Add($"lb_cache_size {cache.Count}");
    return Results.Text(string.Join("\n", lines), "text/plain");
});

app.MapPost("/lb/servers", (BackendServer server) =>
{
    servers.Add(server);
    return Results.Created("/lb/stats", server);
});

app.MapDelete("/lb/servers/{id}", (string id) =>
{
    var server = servers.FirstOrDefault(s => s.Id == id);
    if (server == null) return Results.NotFound();
    servers.Remove(server);
    return Results.NoContent();
});

// ── Main proxy handler ─────────────────────────────────────────

app.Map("{**catch-all}", async (HttpContext context) =>
{
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!rateLimiter.Allow(clientIp))
    {
        context.Response.StatusCode = 429;
        context.Response.Headers["Retry-After"] = "1";
        await context.Response.WriteAsync("Too Many Requests");
        return;
    }

    var cacheKey = $"{context.Request.Method}:{context.Request.Path}{context.Request.QueryString}";
    if (context.Request.Method == "GET" && cache.TryGet(cacheKey, out var cached) && cached != null)
    {
        context.Response.StatusCode = 200;
        context.Response.Headers["X-Cache"] = "HIT";
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(cached);
        return;
    }

    var available = servers.Where(s => s.IsHealthy).ToList();
    var server = strategy.SelectServer(available, context);

    if (server == null)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("No healthy backends available");
        return;
    }

    server.IncrementConnections();
    server.TotalRequests++;

    try
    {
        var targetUrl = $"{server.FullAddress}{context.Request.Path}{context.Request.QueryString}";
        var client = httpFac.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);

        foreach (var header in context.Request.Headers)
        {
            if (!header.Key.StartsWith("Host", StringComparison.OrdinalIgnoreCase) &&
                !header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);

        if (context.Request.ContentLength > 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType != null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.Headers["X-Served-By"] = server.Id;
        context.Response.Headers["X-Strategy"] = strategy.Name;
        context.Response.Headers["X-Cache"] = "MISS";

        if (context.Request.Method == "GET" && response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            cache.Set(cacheKey, body);
            await context.Response.WriteAsync(body);
        }
        else
        {
            await response.Content.CopyToAsync(context.Response.Body);
        }
    }
    catch (HttpRequestException ex)
    {
        server.FailedRequests++;
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync($"Bad Gateway: {ex.Message}");
    }
    finally
    {
        server.DecrementConnections();
    }
});

app.Run();
