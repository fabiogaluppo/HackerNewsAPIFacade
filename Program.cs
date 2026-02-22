using System.Threading.RateLimiting;

using HackerNewsAPIFacade.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IHackerNewsAPI, HackerNewsAPI>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("fixedRate", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknownPartitionKey",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1000,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseRateLimiter();

app.MapGet("/api/beststories", async (int n, IHackerNewsAPI api, CancellationToken ct) =>
{
    if (n <= 0)
        return Results.BadRequest(new { error = "n must be greater than 0" });
    if (n > 250)  
        return Results.BadRequest(new { error = "n must be between 1 and 250" });
    try
    {
        var result = await api.GetBestStoriesAsync(n, ct);
        return Results.Ok(result);
    }
    catch(Exception)
    {
        return Results.Problem(title: "Unexpected error", statusCode: 500);
    }
}).RequireRateLimiting("fixedRate");

app.Run();