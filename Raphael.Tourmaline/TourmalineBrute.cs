using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static Raphael.Tourmaline.Helpers;

namespace Raphael.Tourmaline;

public class TourmalineBrute(string url, string[]? paths = null, int tasks = 32, int delay = -1)
{
    public string Url = ResolveInitialUrl(url);
    public string[] Paths { get; } = paths ?? [];

    public int Tasks { get; set; } = tasks;
    public int Delay { get; set; } = delay;

    private SemaphoreSlim? _rateLimiter;

    public async Task Start(Action<string, HttpStatusCode, long, long, int>? onFound = null)
    {
        Channel<string> channel = Channel.CreateUnbounded<string>();
        HttpClient client = new();

        foreach (string path in Paths)
            await channel.Writer.WriteAsync(ProcessUrl(path, new(Url)) + '/');

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