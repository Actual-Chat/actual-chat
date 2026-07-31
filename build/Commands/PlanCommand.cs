using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Build.Commands;

public class PlanSettings : CommandSettings
{
    [CommandOption("--dry-run")]
    [Description("Print the commands that would run, without running them")]
    public bool IsDryRun { get; init; }
    // Everything after "--". Filled in by PlanCommand before GetPlan runs.
    public string[] ExtraArgs { get; set; } = [];
}

/// <summary>
/// Base class for commands that shell out: they describe what they'd run in
/// <see cref="GetPlan"/>, and this decides whether to run, print or just
/// validate it. Anything wrong with the settings should surface from
/// <c>Validate()</c> or by throwing <see cref="WithoutStackException"/> here.
/// </summary>
public abstract class PlanCommand<TSettings>(CliContext context) : AsyncCommand<TSettings>
    where TSettings : PlanSettings
{
    protected CliContext Context { get; } = context;

    protected abstract CommandPlan GetPlan(TSettings settings);

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken cancellationToken)
    {
        settings.ExtraArgs = [..context.Remaining.Raw];
        var plan = GetPlan(settings);
        Context.LastPlan = plan;
        if (Context.Mode == ExecutionMode.Validate)
            return 0;

        if (Context.Mode == ExecutionMode.Explain || settings.IsDryRun) {
            AnsiConsole.MarkupLine("[grey]Would run:[/]");
            plan.Write();
            return 0;
        }

        return await plan.Run(cancellationToken).ConfigureAwait(false);
    }
}
