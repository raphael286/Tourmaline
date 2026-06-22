using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;

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

            AnsiConsole.Write(table);

            string[] lines = File.ReadAllLines(settings.Path);
            List<(string, string, float, float)> data = [];
            foreach (string line in lines)
            {
                string[] parts = line.Split(' ');
                data.Add((parts[0], parts[1], float.Parse(parts[2]), float.Parse(parts[3])));
            }
            if (settings.SortLength) data = data.OrderByDescending(t => t.Item4).ToList();
            List<string> formatted = data.Select((t) => $"{t.Item1} {t.Item2} {t.Item3} {t.Item4}").ToList();
            File.WriteAllLines(settings.Path, formatted);

            return 0;
        }
    }

}
