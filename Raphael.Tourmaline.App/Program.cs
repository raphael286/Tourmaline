
using Raphael.Tourmaline;

public static class Program
{
    public async static Task Main()
    {
        TourmalineSpider spider = new("http://books.toscrape.com");
        await spider.Start((url, code, left) => Console.WriteLine($"[{code}] {url} ({left} left)"));
    }
}