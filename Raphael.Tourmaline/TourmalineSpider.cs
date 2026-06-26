using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static Raphael.Tourmaline.Helpers;

namespace Raphael.Tourmaline;

public class TourmalineSpider(string url, string[]? known = null, int tasks = 32, int maxDepth = -1, int limit = -1, int delay = -1)
{
    public string Url = ProcessUrl("/", new(ResolveInitialUrl(url)));
    public string[] Known { get; } = known ?? [];

    public int Tasks { get; set; } = tasks;
    public int MaxDepth { get; set; } = maxDepth;
    public int Limit { get; set; } = limit;
    public int Delay { get; set; } = delay;

    public Regex? GoodRegex { get; set; }
    public Regex? BadRegex { get; set; }
    public bool ForceGoodRegex { get; set; }
    public bool ForceBadRegex { get; set; }

    private Uri _uri = new Uri(ProcessUrl("/", new(ResolveInitialUrl(url))));
    private SemaphoreSlim? _rateLimiter;

    public async Task Start(Action<string, HttpStatusCode, long, long, int>? onFound = null)
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
        Action<string, HttpStatusCode, long, long, int>? onFound)
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
            onFound?.Invoke(url, res.StatusCode, time, size, found.Count);

        if (!isReadable) return;

        string content = await res.Content.ReadAsStringAsync();

        foreach (string u in SpiderMatch(content, _uri))
        {
            if (!u.StartsWith(Url)) continue;
            if (ForceGoodRegex && !GoodCheck(u)) continue;
            if (ForceBadRegex && !BadCheck(u)) continue;
            if (!CheckDepth(u, MaxDepth)) continue;
            if (Limit > 0 && found.Count >= Limit) continue;
            if (!found.TryAdd(u.TrimEnd('/'), true)) continue;

            await channel.Writer.WriteAsync(u);
        }
    }

    private bool GoodCheck(string url) => GoodRegex is null || GoodRegex.IsMatch(url);
    private bool BadCheck(string url) => BadRegex is null || !BadRegex.IsMatch(url);
}