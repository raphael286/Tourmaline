using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static Raphael.Tourmaline.Helpers;

namespace Raphael.Tourmaline;

/// <summary>
/// A web spider directory enumerator.
/// </summary>
/// <param name="url">The base URL of the site.</param>
/// <param name="known">The set of paths to check alongside <c>url</c>.</param>
/// <param name="tasks">The number of concurrent tasks to run in the spider.</param>
/// <param name="maxDepth">The maximum amount of URL nesting allowed by the spider.</param>
/// <param name="limit">The maximum number of pages to check.</param>
/// <param name="delay">The delay between requests.</param>
public class TourmalineSpider(string url, string[]? known = null, int tasks = 32, int maxDepth = -1, int limit = -1, int delay = -1)
{
    /// <summary>
    /// The base URL of the site.
    /// </summary>
    public string Url { get; set; } = ResolveInitialUrl(url);

    /// <summary>
    /// The set of paths to check alongside <c>spider.Url</c>.
    /// </summary>
    public string[] Known { get; } = known ?? [];

    /// <summary>
    /// The number of concurrent tasks to run in the spider.
    /// </summary>
    public int Tasks { get; set; } = tasks;

    /// <summary>
    /// The maximum amount of URL nesting allowed by the spider.
    /// <c>-1</c> indicates that the spider should ignore this restriction.
    /// </summary>
    public int MaxDepth { get; set; } = maxDepth;

    /// <summary>
    /// The maximum number of pages to check.
    /// <c>-1</c> indicates that the spider should ignore this restriction.
    /// </summary>
    public int Limit { get; set; } = limit;

    /// <summary>
    /// The delay between requests.
    /// <c>-1</c> indicates that the spider should ignore this restriction.
    /// </summary>
    public int Delay { get; set; } = delay;

    /// <summary>
    /// The regex all URLs must pass to be added to the output.
    /// <c>null</c> indicates that the spider should ignore this restriction.
    /// </summary>
    public Regex? GoodRegex { get; set; }

    /// <summary>
    /// The regex all URLs must fail to be added to the output.
    /// <c>null</c> indicates that the spider should ignore this restriction.
    /// </summary>
    public Regex? BadRegex { get; set; }

    /// <summary>
    /// If <c>true</c>, passing URLs will be added to the queue and failing URLs will not.
    /// This applies <c>GoodRegex</c> to the queue as well as the output.
    /// </summary>
    public bool ForceGoodRegex { get; set; }

    /// <summary>
    /// If <c>true</c>, failing URLs will be added to the queue and passing URLs will not.
    /// This applies <c>BadRegex</c> to the queue as well as the output.
    /// </summary>
    public bool ForceBadRegex { get; set; }

    private Uri _uri = new Uri(ResolveInitialUrl(url));
    private SemaphoreSlim? _rateLimiter;

    /// <summary>
    /// Start's the spider's search, calling <c>onFound</c> with <c>(url, statusCode, responseTimeMS, pageSizeBytes, queueCount)</c> on each page found.
    /// </summary>
    public async Task Start(Action<string, HttpStatusCode, long, long, int> onFound)
    {
        ConcurrentDictionary<string, bool> found = [];
        Channel<string> channel = Channel.CreateUnbounded<string>();
        HttpClient client = new();

        foreach (string k in Known.Prepend(Url))
        {
            found.TryAdd(k.TrimEnd('/'), true);
            await channel.Writer.WriteAsync(k);
        }

        int inFlight = 0;
        _rateLimiter = Delay > 0 ? new SemaphoreSlim(1, 1) : null;

        List<Task> tasks = [];

        for (int i = 0; i < Tasks; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                Stopwatch sw = new();
                await foreach (string url in channel.Reader.ReadAllAsync())
                {
                    Interlocked.Increment(ref inFlight);
                    await Spider(url, channel, found, client, sw, onFound);
                    if (Interlocked.Decrement(ref inFlight) == 0)
                        channel.Writer.TryComplete();
                }
            }));
        }

        await Task.WhenAll(tasks);
        client.Dispose();
        _rateLimiter?.Dispose();
    }

    private async Task Spider(
        string url,
        Channel<string> channel,
        ConcurrentDictionary<string, bool> found,
        HttpClient client,
        Stopwatch sw,
        Action<string, HttpStatusCode, long, long, int> onFound)
    {
        if (_rateLimiter is not null)
        {
            await _rateLimiter.WaitAsync();
            _ = Task.Delay(Delay).ContinueWith(_ => _rateLimiter.Release());
        }

        HttpResponseMessage res;
        sw.Start();
        try { res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead); }
        catch { return; }
        sw.Stop();
        long time = sw.ElapsedMilliseconds;
        long size = res.Content.Headers.ContentLength ?? -1;
        sw.Reset();

        string? contentType = res.Content.Headers.ContentType?.MediaType;

        bool isReadable = contentType is
            "text/html" or "application/xhtml+xml" or
            "application/javascript" or "text/javascript" or "application/x-javascript";

        if (res.StatusCode != HttpStatusCode.NotFound && GoodCheck(url) && BadCheck((url)))
            onFound.Invoke(url, res.StatusCode, time, size, channel.Reader.Count);

        if (!isReadable) return;

        string content = await res.Content.ReadAsStringAsync();

        foreach (string u in SpiderMatch(content, _uri))
        {
            if (!u.StartsWith(Url)) continue;
            if (ForceGoodRegex && !GoodCheck(u)) continue;
            if (ForceBadRegex && !BadCheck(u)) continue;
            if (!CheckDepth(u, MaxDepth)) continue;
            if (Limit > 0 && found.Count >= Limit) continue;
            if (!found.TryAdd(u, true)) continue;

            await channel.Writer.WriteAsync(u);
        }
    }

    private bool GoodCheck(string url) => GoodRegex is null || GoodRegex.IsMatch(url);
    private bool BadCheck(string url) => BadRegex is null || !BadRegex.IsMatch(url);
}