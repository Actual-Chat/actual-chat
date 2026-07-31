using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Build.Commands;

internal static class Server
{
    public const string ProjectPath = "src/dotnet/App.Server/App.Server.csproj";
    public const string PublishDir = "artifacts/publish/App.Server/release";
    public const string DefaultLogPath = "tmp/server.log";
    public const string DefaultUrl = "https://local.voxt.ai/";
    public static readonly TimeSpan OpenDelay = TimeSpan.FromSeconds(10);
    public static string PublishedExeName
        => OperatingSystem.IsWindows() ? "ActualChat.App.Server.exe" : "ActualChat.App.Server";
    public static string PublishedExePath => Path.Combine(PublishDir, PublishedExeName);
}

/// <summary>
/// Runs the server, either from source or from <c>artifacts/publish</c>.
/// Replaces run.cmd, run-second.cmd, server-run-logging.cmd and server-run-published.cmd.
/// </summary>
public sealed class ServerRunCommand(CliContext context) : PlanCommand<ServerRunCommand.Settings>(context)
{
    protected override CommandPlan GetPlan(Settings settings)
    {
        var plan = new CommandPlan();
        var environment = new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Development" };
        if (settings.Urls is { Length: > 0 } urls)
            environment["ASPNETCORE_URLS"] = urls;

        if (settings.LogPath is { } logPath) {
            environment["ActualChat_DevLog"] = Path.GetFullPath(logPath);
            plan.AddAction($"reset the dev log -> {logPath}", _ => ResetLog(logPath));
        }

        if (settings.OpenUrl is { } url)
            plan.AddAction($"open {url} in {Server.OpenDelay.TotalSeconds:F0}s", ct => ScheduleOpen(url, ct));

        if (settings.IsPublished) {
            var exePath = Server.PublishedExePath;
            plan.Add(new RunStep(exePath, settings.ExtraArgs) {
                RequiredPath = exePath,
                Environment = environment,
            });

            return plan;
        }

        string[] passThrough = settings.ExtraArgs.Length == 0 ? [] : ["--", ..settings.ExtraArgs];
        plan.Add(new RunStep(Utils.FindDotnetExe(), [
            "run",
            "-c", settings.Configuration,
            "--no-launch-profile",
            "--project", Server.ProjectPath,
            ..passThrough,
        ]) {
            Environment = environment,
        });

        return plan;
    }

    // Private methods

    private static Task<int> ScheduleOpen(string url, CancellationToken cancellationToken)
    {
        // Fire-and-forget: the server step right after this one blocks until the server exits.
        _ = Task.Run(async () => {
            try {
                await Task.Delay(Server.OpenDelay, cancellationToken).ConfigureAwait(false);
                using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                AnsiConsole.MarkupLine($"[yellow]Couldn't open {url.EscapeMarkup()}: {e.Message.EscapeMarkup()}[/]");
            }
        }, cancellationToken);
        return Task.FromResult(0);
    }

    private static Task<int> ResetLog(string logPath)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(logPath)) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
        if (File.Exists(logPath))
            File.Delete(logPath);

        return Task.FromResult(0);
    }

    // Nested types

    public sealed class Settings : PlanSettings
    {
        [CommandOption("-c|--configuration <CONFIGURATION>")]
        [Description("Debug or Release - ignored with --published")]
        [DefaultValue("Release")]
        public string Configuration { get; init; } = "Release";

        [CommandOption("--published")]
        [Description("Run the published build from artifacts/publish instead of from source")]
        public bool IsPublished { get; init; }

        [CommandOption("--log [PATH]")]
        [Description("Write the dev log to PATH (default: tmp/server.log)")]
        public FlagValue<string> Log { get; init; } = new();

        [CommandOption("--urls <URLS>")]
        [Description("Override ASPNETCORE_URLS, e.g. http://localhost:7086")]
        public string? Urls { get; init; }

        [CommandOption("--open [URL]")]
        [Description("Open the app in a browser once the server is up (default: https://local.voxt.ai/)")]
        public FlagValue<string> Open { get; init; } = new();

        public string? LogPath => !Log.IsSet
            ? null
            : Log.Value is { Length: > 0 } value ? value : Server.DefaultLogPath;
        public string? OpenUrl => !Open.IsSet
            ? null
            : Open.Value is { Length: > 0 } value ? value : Urls ?? Server.DefaultUrl;
    }
}

/// <summary>
/// Publishes the server into <c>artifacts/publish</c>. Replaces server-publish.cmd.
/// </summary>
public sealed class ServerPublishCommand(CliContext context) : PlanCommand<ServerPublishCommand.Settings>(context)
{
    protected override CommandPlan GetPlan(Settings settings)
        => new CommandPlan()
            .AddRun(Utils.FindDotnetExe(), [
                "publish", Server.ProjectPath,
                "-noLogo",
                "-c", settings.Configuration,
                "-p:UseAppPack=false",
                ..settings.ExtraArgs,
            ])
            .AddOutput(Server.PublishedExePath);

    // Nested types

    public sealed class Settings : PlanSettings
    {
        [CommandOption("-c|--configuration <CONFIGURATION>")]
        [Description("Debug or Release")]
        [DefaultValue("Release")]
        public string Configuration { get; init; } = "Release";
    }
}

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
