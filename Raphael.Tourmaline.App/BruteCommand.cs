using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text;
using System.Timers;

namespace Raphael.Tourmaline.App
{
    public class BruteCommand : AsyncCommand<BruteCommand.Settings>
    {

        public class Settings : CommandSettings
        {
            [CommandArgument(0, "<URL>")]
            public required string URL { get; set; }

            [CommandOption("-p|--paths <PATHS-FILE>")]
            public string PathsFile { get; set; } = string.Empty;

            [CommandOption("-t|--tasks <TASKS>")]
            public int Tasks { get; set; } = 32;

            [CommandOption("-d|--delay")]
            public int Delay { get; set; } = -1;

            [CommandOption("-o|--outfile <OUTFILE>")]
            public string Outfile { get; set; } = string.Empty;
        }

        protected async override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            string[] paths;
            if (!string.IsNullOrEmpty(settings.PathsFile)) paths = File.ReadAllLines(settings.PathsFile);
            else
            {
                HttpClient client = new();
                HttpResponseMessage res = await client.GetAsync("https://raw.githubusercontent.com/3ndG4me/KaliLists/refs/heads/master/dirb/common.txt");

                if (res.IsSuccessStatusCode == false)
                {
                    client.Dispose();
                    AnsiConsole.MarkupLine($"The default wordlist didn't return a successful status code.");
                    return -1;
                }
                string content = await res.Content.ReadAsStringAsync();
                paths = content.Split("\n");
                client.Dispose();
            }

            Table table = new();
            table.AddColumns("[green]Tourmaline[/]", Program.VERSION);
            table.Expand();

            table.AddRow("Mode", "[red]Brute[/]");
            table.AddRow("URL", settings.URL);
            table.AddRow("Paths list", !string.IsNullOrEmpty(settings.PathsFile) 
                ? settings.PathsFile 
                : "https://raw.githubusercontent.com/3ndG4me/KaliLists/refs/heads/master/dirb/common.txt");
            table.AddRow("Tasks", settings.Tasks.ToString());
            table.AddEmptyRow();
            if (settings.Delay != -1) table.AddRow("Delay", settings.Delay.ToString());
            if (!string.IsNullOrEmpty(settings.Outfile)) table.AddRow("Outfile", settings.Outfile);
            AnsiConsole.Write(table);

            TourmalineBrute brute = new(settings.URL, paths, settings.Tasks, settings.Delay);
            List<(string, HttpStatusCode, long, long)> found = [];
            Action<string, HttpStatusCode, long, long, int> onFound = (url, code, time, size, count) =>
            {
                AnsiConsole.MarkupLine($"[[{(int)code} {code} - {time}ms, {size / 1024.0:F1}kb]] [green]{url}[/] ({count} left)");
                found.Add((url, code, time, size));
            };

            await brute.Start(onFound);

            if (!string.IsNullOrEmpty(settings.Outfile))
            {
                // url, code, time, size
                List<string> formatted = found.Select((t) => $"{t.Item1} {(int)t.Item2} {t.Item3} {t.Item4}").ToList();
                File.WriteAllLines(settings.Outfile, formatted);
            }

            return 0;
        }
    }
}
