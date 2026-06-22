using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Raphael.Tourmaline.App
{
    public class SpiderCommand : AsyncCommand<SpiderCommand.Settings>
    {

        public class Settings : CommandSettings
        {
            [CommandArgument(0, "<URL>")]
            public required string URL { get; set; }

            [CommandOption("-k|--known <KNOWN-FILE>")]
            public string KnownFile { get; set; } = string.Empty;

            [CommandOption("-t|--tasks <TASKS>")]
            public int Tasks { get; set; } = 32;

            [CommandOption("-m|--max-depth <MAX-DEPTH>")]
            public int MaxDepth { get; set; } = -1;

            [CommandOption("-l|--limit <LIMIT>")]
            public int Limit { get; set; } = 500;

            [CommandOption("-g|--good <GOOD-REGEX>")]
            public string GoodRegex { get; set; } = string.Empty;

            [CommandOption("-b|--bad <BAD-REGEX>")]
            public string BadRegex { get; set; } = string.Empty;

            [CommandOption("--force-good")]
            public bool ForceGoodRegex { get; set; } = false;

            [CommandOption("--force-bad")]
            public bool ForceBadRegex { get; set; } = false;
        }

        protected async override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            List<string> known = [];
            if (settings.KnownFile != string.Empty) known.AddRange(File.ReadAllLines(settings.KnownFile));
            known.AddRange(["sitemap.xml", "robots.txt"]);

            Table table = new();
            table.AddColumns("[green]Tourmaline[/]", Program.VERSION);
            table.Expand();

            table.AddRow("Mode", "Spider");
            table.AddRow("URL", settings.URL);
            table.AddRow("Tasks", settings.Tasks.ToString());
            table.AddEmptyRow();

            if (settings.MaxDepth != -1) table.AddRow("Max Depth", settings.MaxDepth.ToString());
            if (settings.Limit != -1) table.AddRow("Limit", settings.Limit.ToString());
            if (!string.IsNullOrEmpty(settings.GoodRegex)) table.AddRow("Good Regex", settings.GoodRegex.Replace("[", "[[").Replace("]", "]]"));
            if (!string.IsNullOrEmpty(settings.BadRegex)) table.AddRow("Bad Regex", settings.BadRegex.Replace("[", "[[").Replace("]", "]]"));
            if (settings.ForceGoodRegex) table.AddRow("Force Good Regex", "true");
            if (settings.ForceBadRegex) table.AddRow("Force Bad Regex", "true");
            

            table.AddRow("Known Paths", known.Count == 0 ? "No known paths specified." : string.Join(", ", known));
            AnsiConsole.Write(table);

            TourmalineSpider spider = new(settings.URL, [.. known], settings.Tasks, settings.MaxDepth, settings.Limit);
            spider.GoodRegex = !string.IsNullOrEmpty(settings.GoodRegex) ? new(settings.GoodRegex) : null;
            spider.BadRegex = !string.IsNullOrEmpty(settings.BadRegex) ? new(settings.BadRegex) : null;
            spider.ForceGoodRegex = settings.ForceGoodRegex;
            spider.ForceBadRegex = settings.ForceBadRegex;

            Action<string, HttpStatusCode, long, long, int> onFound = (url, code, time, size, count) =>
                AnsiConsole.MarkupLine($"[[{code} - {time}ms, {size / 1024.0:F1}kb]] [green]{url}[/] ({count} left)");

            await spider.Start(onFound);
            return 0;
        }
    }
}
