using Microsoft.Extensions.Caching.Memory;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace HackerNewsAPIFacade.Services;

public interface IHackerNewsAPI
{
    Task<IReadOnlyList<Models.BestStory>> GetBestStoriesAsync(int n, CancellationToken ct);
}

public sealed class HackerNewsAPI : IHackerNewsAPI
{
    private static readonly string HackerNewsApiUriPrefix = "https://hacker-news.firebaseio.com/v0";
    private static readonly TimeSpan BestStoriesIdsTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ItemTtl = TimeSpan.FromSeconds(30);
    private static readonly int NumberOfConcurrentCallsAllowed = 2 * Environment.ProcessorCount;

    private readonly ILogger<HackerNewsAPI> log_;
    private readonly HttpClient httpClient_;
    private readonly IMemoryCache cache_;
    private readonly SemaphoreSlim concurrencyLimiter_ = new(NumberOfConcurrentCallsAllowed, NumberOfConcurrentCallsAllowed);

    public HackerNewsAPI(ILogger<HackerNewsAPI> log, HttpClient httpClient, IMemoryCache cache)
    {
        log_ = log;
        httpClient_ = httpClient;
        cache_ = cache;
    }

    public async Task<IReadOnlyList<Models.BestStory>> GetBestStoriesAsync(int n, CancellationToken ct)
    {
        try
        {
            //Without cache
            //var ids = await GetIDsFromBestStoriesAsync(ct);
            //With cache
            var ids = await CachedGetIDsFromBestStoriesAsync(ct);
            if (ids != null)
            {
                var tasks = ids.Take(Math.Min(ids.Length, n))
                               .Select(id => {
                                    //Without cache
                                    //return GetItemByIDAsync(id, ct);
                                    //With cache
                                    return CachedGetItemByIDAsync(id, ct);
                                });
                var results = await Task.WhenAll(tasks);
                return FilterModels(results, ct);
            }
            return Array.Empty<Models.BestStory>().AsReadOnly();
        }
        catch (Exception ex)
        {
            log_.LogError(ex, "Internal error");
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlyCollection<Models.BestStory> FilterModels(Models.BestStory?[] items, CancellationToken ct, bool sortByScoreDesc = false)
    {
        //LINQ as alternative implementation here
        if (ct.IsCancellationRequested)
            return Array.Empty<Models.BestStory>().AsReadOnly();
        var temp = new List<Models.BestStory>(items.Length);
        //filter
        foreach(var item in items)
            if (item != null)
                temp.Add(item);
        //sort - it seems the api returns score by descending order, just in case:
        if (sortByScoreDesc)
            temp.Sort((lhs, rhs) => rhs.Score - lhs.Score);
        return temp.AsReadOnly();
    }

    private async Task<long[]?> GetIDsFromBestStoriesAsync(CancellationToken ct)
    {
        var uri = $"{HackerNewsApiUriPrefix}/beststories.json";
        await concurrencyLimiter_.WaitAsync(ct);
        try
        {
            //Without retry
            //return await httpClient_.GetFromJsonAsync<long[]>(uri, ct);
            //With retry
            return await RetrierAsync(() => httpClient_.GetFromJsonAsync<long[]>(uri, ct), ct);
        }
        finally
        {
            concurrencyLimiter_.Release();
        }
    }   

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<long[]?> CachedGetIDsFromBestStoriesAsync(CancellationToken ct)
    {
        var key = "items";
        if (cache_.TryGetValue(key, out long[]? cached))
            return cached;
        var items = await GetIDsFromBestStoriesAsync(ct);
        if (!ct.IsCancellationRequested)
        {
            var expiration = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = BestStoriesIdsTtl
            };
            cache_.Set(key, items, expiration);
        }
        return items;
    }

    // private record HackerNewsItem(long Id, bool Deleted, string? Type, string? By, long Time, string? Text, string? Title, 
    //                               bool Dead, long Parent, string? Url, int Score, int Descendants); //add or remove item properties
    private record HackerNewsItem(long Id, string? Type, string? Title, string? Url, string? By, long Time, int Score, int Descendants);
    
    private async Task<Models.BestStory?> GetItemByIDAsync(long id, CancellationToken ct)
    {
        var uri = new Uri($"{HackerNewsApiUriPrefix}/item/{id}.json");
        await concurrencyLimiter_.WaitAsync(ct);
        try
        {
            //Without retry
            //var result = await httpClient_.GetFromJsonAsync<HackerNewsItem?>(uri, ct);
            //With retry
            var result = await RetrierAsync(() => httpClient_.GetFromJsonAsync<HackerNewsItem?>(uri, ct), ct);
            return TransformItemToModel(result, ct, onlyStoryType: true);
        }
        finally
        {
            concurrencyLimiter_.Release();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Models.BestStory? TransformItemToModel(HackerNewsItem? item, CancellationToken ct, bool onlyStoryType = true)
    {
        //LINQ as alternative implementation here
        if (!ct.IsCancellationRequested && item != null &&
           onlyStoryType && string.Equals(item.Type, "story", StringComparison.OrdinalIgnoreCase))
        {
            return new Models.BestStory
            (
                Title: item.Title ?? "",
                Uri: item.Url ?? "",
                PostedBy: item.By ?? "",
                Time: DateTimeOffset.FromUnixTimeSeconds(item.Time).ToUniversalTime(),
                Score: item.Score,
                CommentCount: item.Descendants
            );
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<Models.BestStory?> CachedGetItemByIDAsync(long id, CancellationToken ct)
    {
        var key = $"item:{id}";
        if (cache_.TryGetValue(key, out Models.BestStory? cached))
            return cached;
        var item = await GetItemByIDAsync(id, ct);
        if (!ct.IsCancellationRequested)
        {
            var expiration = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ItemTtl
            };
            cache_.Set(key, item, expiration);
        }
        return item;
    }

    private static readonly TimeSpan[] Delays = [ TimeSpan.FromMilliseconds(100),
                                                  TimeSpan.FromMilliseconds(200),
                                                  TimeSpan.FromMilliseconds(350)];
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task<T?> RetrierAsync<T>(Func<Task<T?>> func, CancellationToken ct)
    {
        for (int i = 0; i < Delays.Length; i++)
        {
            try
            {
                return await func();
            }
            catch (HttpRequestException ex) 
            when (!ct.IsCancellationRequested && i < Delays.Length)
            {
                log_.LogWarning(ex, "retrying...");
                await Task.Delay(Delays[i], ct);
            }
            catch (TaskCanceledException ex)
            when (!ct.IsCancellationRequested && i < Delays.Length)
            {
                log_.LogWarning(ex, "retrying...");
                await Task.Delay(Delays[i], ct);
            }
            catch (Exception)
            {
                throw;
            }
        }
        return default;
    }
}