using System.ComponentModel;
using Spectre.Console.Cli;

namespace Build.Commands;

/// <summary>
/// Starts <c>server-loop.ps1</c>. The loop itself stays in the script - the
/// /server-loop skill and other tooling reference it directly.
/// </summary>
public sealed class ServerLoopCommand(CliContext context) : PlanCommand<ServerLoopCommand.Settings>(context)
{
    protected override CommandPlan GetPlan(Settings settings)
        => new CommandPlan().AddRun("pwsh", [
            "-NoProfile",
            "-File", "server-loop.ps1",
            "-c", settings.Configuration,
            ..settings.ExtraArgs,
        ]);

    // Nested types

    public sealed class Settings : PlanSettings
    {
        [CommandOption("-c|--configuration <CONFIGURATION>")]
        [Description("Debug or Release")]
        [DefaultValue("Release")]
        public string Configuration { get; init; } = "Release";
    }
}
