using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static Raphael.Tourmaline.Helpers;

namespace Raphael.Tourmaline;

/// <summary>
/// A brute force directory enumerator.
/// </summary>
/// <param name="url">The base URL of the site.</param>
/// <param name="paths">The set of paths to check relative to <c>url</c>.</param>
/// <param name="tasks">The number of concurrent tasks to run in the brute.</param>
/// <param name="delay">The delay between requests.</param>
public class TourmalineBrute(string url, string[]? paths = null, int tasks = 32, int delay = -1)
{
    /// <summary>
    /// The base URL of the site.
    /// </summary>
    public string Url = ResolveInitialUrl(url);

    /// <summary>
    /// The set of paths to check relative to <c>brute.Url</c>.
    /// Initializes to an empty array if <c>null</c> is passed to the constructor.
    /// </summary>
    public string[] Paths { get; } = paths ?? [];

    /// <summary>
    /// The number of concurrent tasks to run in the brute.
    /// </summary>
    public int Tasks { get; set; } = tasks;

    /// <summary>
    /// The delay between requests.
    /// <c>-1</c> indicates that the brute should ignore this restriction.
    /// </summary>
    public int Delay { get; set; } = delay;

    private SemaphoreSlim? _rateLimiter;

    /// <summary>
    /// Start's the brute's search, calling <c>onFound</c> with <c>(url, statusCode, responseTimeMS, pageSizeBytes, queueCount)</c> on each page found.
    /// </summary>
    public async Task Start(Action<string, HttpStatusCode, long, long, int>? onFound = null)
    {
        Channel<string> channel = Channel.CreateUnbounded<string>();
        HttpClient client = new();

        foreach (string path in Paths)
            await channel.Writer.WriteAsync(ProcessUrl(path, new(Url)));

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
                    await Brute(url, channel, client, sw, onFound);
                    if (Interlocked.Decrement(ref inFlight) == 0)
                        channel.Writer.TryComplete();
                }
            }));
        }

        await Task.WhenAll(tasks);
        client.Dispose();
        _rateLimiter?.Dispose();
    }

    private async Task Brute(
        string url,
        Channel<string> channel,
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

        if (res.StatusCode != HttpStatusCode.NotFound)
            onFound?.Invoke(url, res.StatusCode, time, size, channel.Reader.Count);
    }
}