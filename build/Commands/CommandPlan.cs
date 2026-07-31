using CliWrap;
using Microsoft.Extensions.FileSystemGlobbing;
using Spectre.Console;
using static Build.CliWrapCommandExtensions;

namespace Build.Commands;

public enum ExecutionMode
{
    Run = 0,
    Explain,
    Validate,
}

/// <summary>
/// Shared state for a single <c>b</c> invocation. Injected into commands, so the
/// interactive UI can re-run them in <see cref="ExecutionMode.Validate"/> mode
/// to check a command and read back its plan without executing anything.
/// </summary>
public sealed class CliContext
{
    public ExecutionMode Mode { get; set; } = ExecutionMode.Run;
    public CommandPlan? LastPlan { get; set; }
}

public abstract record PlanStep;

public sealed record RunStep(string Executable, IReadOnlyList<string> Arguments) : PlanStep
{
    public string? WorkingDirectory { get; init; }
    public string? RequiredPath { get; init; }
}

public sealed record ActionStep(string Description, Func<CancellationToken, Task<int>> Run) : PlanStep;

// Reports where the artifact a command produced ended up. The path may be a glob
// (e.g. "*.pkg" or a version-stamped .msix), resolved when the step is reached.
public sealed record OutputStep(string Path) : PlanStep;

/// <summary>
/// What a command is going to run, built before anything executes. Commands
/// produce one in <c>GetPlan</c>; whether it's printed, validated or executed
/// is decided by <see cref="CliContext.Mode"/>.
/// </summary>
public sealed class CommandPlan
{
    private List<string> Secrets { get; } = [];
    public List<PlanStep> Steps { get; } = [];

    public CommandPlan Add(PlanStep step)
    {
        Steps.Add(step);
        return this;
    }

    public CommandPlan AddRun(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null)
        => Add(new RunStep(executable, arguments) { WorkingDirectory = workingDirectory });

    public CommandPlan AddAction(string description, Func<CancellationToken, Task<int>> run)
        => Add(new ActionStep(description, run));

    public CommandPlan AddOutput(string path)
        => Add(new OutputStep(path));

    public CommandPlan AddSecret(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Secrets.Add(value);

        return this;
    }

    public string Describe(PlanStep step)
    {
        switch (step) {
        case OutputStep x:
            return $"[grey]->[/] [aqua]{ToRelative(x.Path).EscapeMarkup()}[/]";
        case ActionStep x:
            return $"[grey]$[/] {x.Description.EscapeMarkup()}";
        case RunStep x: {
            var line = string.Join(' ', [x.Executable, ..x.Arguments]);
            foreach (var secret in Secrets)
                line = line.Replace(secret, "***");

            var prefix = x.WorkingDirectory is { } workingDirectory ? $"[grey]({workingDirectory})[/] " : "";
            return $"[grey]$[/] {prefix}{line.EscapeMarkup()}";
        }
        default:
            return step.ToString() ?? "";
        }
    }

    public void Write()
    {
        foreach (var step in Steps)
            AnsiConsole.MarkupLine("  " + Describe(step));
    }

    public async Task<int> Run(CancellationToken cancellationToken)
    {
        foreach (var step in Steps) {
            if (step is OutputStep output) {
                WriteOutput(output);
                continue;
            }

            AnsiConsole.MarkupLine(Describe(step));
            var exitCode = step switch {
                ActionStep x => await x.Run(cancellationToken).ConfigureAwait(false),
                RunStep x => await RunStepAsync(x, cancellationToken).ConfigureAwait(false),
                _ => 0,
            };
            if (exitCode != 0)
                return exitCode;
        }

        return 0;
    }

    // Private methods

    private static void WriteOutput(OutputStep step)
    {
        var matches = Resolve(step.Path);
        if (matches.Count == 0) {
            AnsiConsole.MarkupLine($"[yellow]Expected output not found:[/] {ToRelative(step.Path).EscapeMarkup()}");
            return;
        }

        foreach (var match in matches) {
            var size = new FileInfo(match).Length / (1024.0 * 1024.0);
            AnsiConsole.MarkupLine($"[green]Output:[/] [aqua]{ToRelative(match).EscapeMarkup()}[/] [grey]({size:F1} MB)[/]");
        }
    }

    private static IReadOnlyList<string> Resolve(string path)
    {
        if (!path.Contains('*'))
            return File.Exists(path) ? [path] : [];

        var root = Directory.GetCurrentDirectory();
        var matcher = new Matcher();
        matcher.AddInclude(path.Replace('\\', '/'));
        return [..matcher.GetResultsInFullPath(root)];
    }

    private static string ToRelative(string path)
        => Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');

    private static async Task<int> RunStepAsync(RunStep step, CancellationToken cancellationToken)
    {
        if (step.RequiredPath is { } requiredPath && Resolve(requiredPath).Count == 0)
            throw new WithoutStackException($"Not found: {ToRelative(requiredPath)}");

        var command = Cli.Wrap(step.Executable).WithArguments(step.Arguments, false);
        if (step.WorkingDirectory is { } workingDirectory)
            command = command.WithWorkingDirectory(workingDirectory);

        var result = await command
            .ToConsole()
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync(cancellationToken)
            .Task
            .ConfigureAwait(false);

        return result.ExitCode;
    }
}
