using System.ComponentModel;
using Bullseye;
using Spectre.Console.Cli;

namespace Build.Commands;

/// <summary>
/// The default command: runs Bullseye targets. Keeps the option surface CI and
/// <c>run-build.cmd</c> already rely on, so <c>b pack-ios --configuration Release</c>
/// behaves exactly as before.
/// </summary>
public sealed class TargetsCommand(CliContext cliContext) : AsyncCommand<TargetsCommand.Settings>
{
    private CliContext CliContext { get; } = cliContext;

    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (CliContext.Mode != ExecutionMode.Run) {
            CliContext.LastPlan = new CommandPlan()
                .AddAction($"bullseye {string.Join(' ', settings.Targets)}", _ => Task.FromResult(0));
            return Task.FromResult(0);
        }

        return Program.RunTargets(
            settings.Targets,
            settings.MustClear,
            settings.IsDryRun,
            settings.Host ?? default,
            settings.MustListDependencies,
            settings.MustListInputs,
            settings.MustListTargets,
            settings.MustListTree,
            settings.HasNoColor,
            settings.HasNoExtendedChars,
            settings.IsParallel,
            settings.MustSkipDependencies,
            settings.IsVerbose,
            cancellationToken,
            settings.Configuration,
            settings.IsDevMaui,
            settings.UseNativeAot ?? false,
            settings.HasDumps,
            settings.IsUniversal);
    }

    // Nested types

    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[TARGETS]")]
        [Description("Bullseye targets to run, e.g. build, watch, restore, pack-ios")]
        public string[] Targets { get; init; } = [];

        [CommandOption("-c|--configuration <CONFIGURATION>")]
        [Description("Debug or Release")]
        [DefaultValue("Debug")]
        public string Configuration { get; init; } = "Debug";

        [CommandOption("--is-dev-maui <VALUE>")]
        [Description("false - the app connects to voxt.ai, true - to dev.voxt.ai")]
        public bool? IsDevMaui { get; init; }

        [CommandOption("--use-native-aot <VALUE>")]
        [Description("Build the MAUI app with Native AOT (currently wired for pack-ios only)")]
        public bool? UseNativeAot { get; init; }

        [CommandOption("--universal")]
        [Description("pack-mac only: build a universal arm64 + x64 bundle instead of one for the host CPU")]
        public bool IsUniversal { get; init; }

        [CommandOption("--dumps")]
        [Description("Enable test running crash dumps")]
        public bool HasDumps { get; init; }

        [CommandOption("--list-targets")]
        [Description("List all (or specified) targets, then exit")]
        public bool MustListTargets { get; init; }

        [CommandOption("--list-tree")]
        [Description("List all (or specified) targets and dependency trees, then exit")]
        public bool MustListTree { get; init; }

        [CommandOption("--list-dependencies")]
        [Description("List all (or specified) targets and dependencies, then exit")]
        public bool MustListDependencies { get; init; }

        [CommandOption("--list-inputs")]
        [Description("List all (or specified) targets and inputs, then exit")]
        public bool MustListInputs { get; init; }

        [CommandOption("--skip-dependencies")]
        [Description("Do not run targets' dependencies")]
        public bool MustSkipDependencies { get; init; }

        [CommandOption("--parallel")]
        [Description("Run targets in parallel")]
        public bool IsParallel { get; init; }

        [CommandOption("--dry-run")]
        [Description("Do a dry run without executing actions")]
        public bool IsDryRun { get; init; }

        [CommandOption("--verbose")]
        [Description("Enable verbose output")]
        public bool IsVerbose { get; init; }

        [CommandOption("--clear")]
        [Description("Clear the console before execution")]
        public bool MustClear { get; init; }

        [CommandOption("--no-color")]
        [Description("Disable colored output")]
        public bool HasNoColor { get; init; }

        [CommandOption("--no-extended-chars")]
        [Description("Disable extended characters")]
        public bool HasNoExtendedChars { get; init; }

        [CommandOption("--host <HOST>")]
        [Description("Force the mode for a specific host environment (normally auto-detected)")]
        public Host? Host { get; init; }
    }
}
