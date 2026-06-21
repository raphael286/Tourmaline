using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using static Raphael.Tourmaline.Helpers;

namespace Raphael.Tourmaline;

public class TourmalineSpider(string url, string[]? known = null)
{
    public string Url = ProcessUrl("/", new(url));
    public string[] Known { get; } = known ?? [];

    public int Tasks { get; } = 16;
    public int MaxDepth { get; } = -1;
    public int Limit { get; } = -1;

    private Uri _uri = new Uri(ProcessUrl("/", new(url)));

    public async Task<List<string>> Start(Action<string, HttpStatusCode, int>? onFound = null)
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

        List<Task> tasks = [];

        for (int i = 0; i < Tasks; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await foreach (string url in channel.Reader.ReadAllAsync())
                {
                    Interlocked.Increment(ref inFlight);
                    await Spider(url, channel, found, client, onFound);

                    if (Interlocked.Decrement(ref inFlight) == 0)
                        channel.Writer.TryComplete();
                }
            }));
        }

        await Task.WhenAll(tasks);
        client.Dispose();
        return found.Keys.ToList();
    }

    private async Task Spider(
        string url,
        Channel<string> channel,
        ConcurrentDictionary<string, bool> found,
        HttpClient client,
        Action<string, HttpStatusCode, int>? onFound)
    {
        HttpResponseMessage res;
        try { res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead); }
        catch { return; }

        string? contentType = res.Content.Headers.ContentType?.MediaType;

        bool isHtml = contentType is "text/html" or "application/xhtml+xml";

        if (res.StatusCode != HttpStatusCode.NotFound)
            onFound?.Invoke(url, res.StatusCode, found.Count);

        if (!isHtml) return;

        string content = await res.Content.ReadAsStringAsync();

        foreach (string u in SpiderMatch(content, _uri))
        {
            if (!u.StartsWith(Url)) continue;
            if (!CheckDepth(u, MaxDepth)) continue;
            if (!found.TryAdd(u.TrimEnd('/'), true)) continue;

            await channel.Writer.WriteAsync(u);
        }
    }
}