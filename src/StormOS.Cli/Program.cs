using System.CommandLine;
using StormOS.Cli;
using StormOS.Cli.Commands;

// storm: STORM OS command line. Read-only by default; every change requires confirmation (or --yes).
await using var services = CliHost.Build();
var json = new Option<bool>("--json") { Description = "Machine readable JSON output.", Recursive = true };
var root = new RootCommand("STORM OS — Windows gaming performance command center.")
{
    json,
    InfoCommands.Status(services),
    InfoCommands.Hardware(services),
    InfoCommands.Games(services),
    ActionCommands.Benchmark(services),
    ActionCommands.Optimize(services),
    ActionCommands.Restore(services),
    ActionCommands.Network(services),
    ActionCommands.Scan(services),
    ActionCommands.Logs(services),
    ActionCommands.Service(),
};

var parse = root.Parse(args);
Output.Json = parse.GetValue(json);
return await parse.InvokeAsync();
