using Spectre.Console.Cli;

namespace Raphael.Tourmaline.App
{
    public static class Program
    {
        public const string VERSION = "v0.0.0";

        public async static Task<int> Main(string[] args)
        {
            CommandApp app = new();
            app.Configure(c =>
            {
                c.AddCommand<SpiderCommand>("spider");
                c.AddCommand<HandlerCommand>("handler");
            });

            return await app.RunAsync(args);
        }
    }
}