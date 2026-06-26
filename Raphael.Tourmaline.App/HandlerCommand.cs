using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Raphael.Tourmaline.App
{
    public class HandlerCommand : Command<HandlerCommand.Settings>
    { 
        public class Settings : CommandSettings
        {
            [CommandArgument(0, "<PATH>")]
            public required string Path { get; set; }

            [CommandOption("-l|--sort-length")]
            public bool SortLength { get; set; }

            [CommandOption("-t|--sort-time")]
            public bool SortTime { get; set; }

            [CommandOption("-c|--sort-code")]
            public bool SortCode { get; set; }

            [CommandOption("-a|--sort-alphabetical")]
            public bool SortAlphabetical { get; set; }

            [CommandOption("--min-size <MIN-SIZE>")]
            public long MinSize { get; set; } = 0;

            [CommandOption("--max-size <MAX-SIZE>")]
            public long MaxSize { get; set; } = long.MaxValue;

            [CommandOption("--min-time <MIN-TIME>")]
            public long MinTime { get; set; } = 0;

            [CommandOption("--max-time <MAX-TIME>")]
            public long MaxTime { get; set; } = long.MaxValue;

            [CommandOption("--code <CODE>")]
            public int Code { get; set; } = -1;

            [CommandOption("-r|--regex <REGEX>")]
            public string Regex { get; set; } = string.Empty;

            [CommandOption("--csv")]
            public bool Csv { get; set; }

            [CommandOption("-s|--strip")]
            public bool Strip { get; set; }
        }
        protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            Table table = new();
            table.AddColumns("[green]Tourmaline[/]", Program.VERSION);
            table.Expand();

            table.AddRow("Mode", "Handler");
            table.AddRow("Path", settings.Path);
            table.AddEmptyRow();

            if (settings.SortLength) table.AddRow("Sort", "By length");
            if (settings.SortTime) table.AddRow("Sort", "By response time");
            if (settings.SortCode) table.AddRow("Sort", "By response code");
            if (settings.SortAlphabetical) table.AddRow("Sort", "By URL alphabetical");
            if (settings.MinSize != 0) table.AddRow("Min Size", settings.MinSize.ToString());
            if (settings.MaxSize != long.MaxValue) table.AddRow("Max Size", settings.MaxSize.ToString());
            if (settings.MinTime != 0) table.AddRow("Min Time", settings.MinTime.ToString());
            if (settings.MaxTime != long.MaxValue) table.AddRow("Max Time", settings.MaxTime.ToString());
            if (settings.Code != -1) table.AddRow("Code", settings.Code.ToString());
            if (!string.IsNullOrEmpty(settings.Regex)) table.AddRow("Regex", settings.Regex);
            if (settings.Csv) table.AddRow("Output", "CSV");
            if (settings.Strip) table.AddRow("Strip", "Enabled");

            AnsiConsole.Write(table);

            string[] lines = File.ReadAllLines(settings.Path);
            List<(string, int, long, long)> data = [];
            foreach (string line in lines)
            {
                string[] parts = line.Split(' ');
                data.Add((parts[0], int.Parse(parts[1]), long.Parse(parts[2]), long.Parse(parts[3])));
            }

            if (settings.SortLength) data = [.. data.OrderByDescending(t => t.Item4)];
            if (settings.SortTime) data = [.. data.OrderByDescending(t => t.Item3)];
            if (settings.SortCode) data = [.. data.OrderByDescending(t => t.Item2)];
            if (settings.SortAlphabetical) data = [.. data.OrderBy(t => t.Item1)];

            data = [.. data.Where(t => t.Item4 >= settings.MinSize)];
            data = [.. data.Where(t => t.Item4 <= settings.MaxSize)];
            data = [.. data.Where(t => t.Item3 >= settings.MinTime)];
            data = [.. data.Where(t => t.Item3 <= settings.MaxTime)];
            if (settings.Code != -1) data = [.. data.Where(t => t.Item2 == settings.Code)];

            Regex regex = new(settings.Regex);
            if (!string.IsNullOrEmpty(settings.Regex)) data = [.. data.Where(t => regex.IsMatch(t.Item1))];

            List<string> formatted = settings.Strip
                ? [.. data.Select(t => t.Item1)]
                : [.. data.Select((t) => $"{t.Item1} {t.Item2} {t.Item3} {t.Item4}")];
            File.WriteAllLines(settings.Path, formatted);

            return 0;
        }
    }

}
