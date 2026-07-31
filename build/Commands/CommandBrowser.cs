using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;

namespace Build.Commands;

/// <summary>
/// The interactive UI. Everything it shows - commands, arguments, options,
/// defaults, descriptions - is read from the model Spectre builds from the
/// command registrations, so the two can't drift apart.
/// </summary>
internal sealed class CommandBrowser(Func<string[], Task<int>> dispatch, ICommandModel model, CliContext context)
{
    private static readonly string RunNow = "[green]<run>[/]";
    private static readonly string Blocked = "[red]<run - blocked>[/]";
    private static readonly string Back = "..";
    private bool _isQuitting;
    private Func<string[], Task<int>> Dispatch { get; } = dispatch;
    private ICommandModel Model { get; } = model;
    private CliContext Context { get; } = context;

    public async Task<int> Run(CancellationToken cancellationToken)
    {
        if (!AnsiConsole.Profile.Capabilities.Interactive) {
            AnsiConsole.MarkupLine("[yellow]This terminal isn't interactive - showing the command tree instead.[/]");
            AnsiConsole.MarkupLine("[grey]Run [/][aqua]b --help[/][grey] or [/][aqua]b tree -o[/][grey] for details.[/]");
            TreeCommand.Model = Model;
            return await Dispatch(["tree"]).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested) {
            var command = SelectCommand(Model, []);
            if (command is null)
                return 0;

            var args = await EditArguments(command.Info, command.Path).ConfigureAwait(false);
            if (_isQuitting)
                return 0;
            if (args is null)
                continue;

            WriteHeader(args, null, null);
            var exitCode = await Dispatch(args).ConfigureAwait(false);
            AnsiConsole.MarkupLine(exitCode == 0 ? "[green]-- OK --[/]" : $"[red]-- exit code {exitCode} --[/]");
            AnsiConsole.Markup("[grey]Press any key to return to the menu...[/]");
            AnsiConsole.Console.Input.ReadKey(true);
        }

        return 0;
    }

    // Private methods

    private static void WriteHeader(string[] args, string? error, CommandPlan? plan)
    {
        // The header is the only thing that scrolls: it's redrawn in place above the
        // prompt on every selection, so picking options doesn't fill the console.
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("b").Color(Color.Aqua));
        AnsiConsole.Write(new Rule($"[aqua]b {string.Join(' ', args)}[/]").LeftJustified());
        if (error is not null)
            AnsiConsole.MarkupLine($"[red]x[/] {error.EscapeMarkup()}");
        else
            plan?.Write();
    }

    private SelectedCommand? SelectCommand(ICommandContainer container, string[] path)
    {
        while (true) {
            var commands = container.Commands.Where(x => !x.IsHidden && !x.IsDefaultCommand);
            List<object> choices = [Back, ..commands];
            if (container is ICommandModel { DefaultCommand: { } defaultCommand })
                choices.Add(new SelectedCommand(defaultCommand, path));

            WriteHeader(path, null, null);
            var backLabel = path.Length == 0
                ? $"[grey]{Prompts.Arrow} quit[/] [grey35](b)[/]"
                : $"[grey]{Prompts.Arrow} back[/] [grey35](b)[/]";
            var (choice, shortcut) = Prompts.Show(
                new SelectionPrompt<object>()
                    .Title("[green]Pick a command[/]")
                    .PageSize(20)
                    .UseConverter(x => ReferenceEquals(x, Back) ? backLabel : Describe(x))
                    .AddChoices(choices),
                Prompts.BackKeys);
            if (shortcut == Prompts.QuitShortcut)
                _isQuitting = true;

            if (shortcut is not null)
                return null;

            switch (choice) {
            case string:
                return null;
            case SelectedCommand selected:
                return selected;
            case ICommandInfo { IsBranch: true } branch: {
                var selected = SelectCommand(branch, [..path, branch.Name]);
                if (selected is not null || _isQuitting)
                    return selected;

                continue;
            }
            case ICommandInfo info:
                return new SelectedCommand(info, [..path, info.Name]);
            }
        }
    }

    private static string Describe(object choice)
        => choice switch {
            SelectedCommand x => $"[yellow]<targets>[/]  [grey]{x.Info.Description}[/]",
            ICommandInfo x when x.IsBranch => $"[aqua]{x.Name}[/] [grey]...[/]  [grey]{x.Description}[/]",
            ICommandInfo x => $"[aqua]{x.Name}[/]  [grey]{x.Description}[/]",
            _ => choice.ToString() ?? "",
        };

    private async Task<string[]?> EditArguments(ICommandInfo info, string[] path)
    {
        var parameters = info.Parameters.Where(x => !x.IsHidden).Select(x => new ParameterState(x)).ToList();
        var required = parameters.Where(x => x is { Parameter: ICommandArgument, Parameter.IsRequired: true });
        foreach (var state in required) {
            var (isBack, value) = PromptValue(state, path);
            if (isBack)
                return null;

            state.Value = value;
        }

        while (true) {
            var args = BuildArgs(path, parameters);
            var check = await Validate(args).ConfigureAwait(false);
            WriteHeader(args, check.Error, check.Plan);
            var isRunnable = check.Error is null;
            List<object> choices = [Back, isRunnable ? RunNow : Blocked, ..parameters];
            var (choice, shortcut) = Prompts.Show(
                new SelectionPrompt<object>()
                    .Title($"[green]{info.Description}[/]")
                    .PageSize(20)
                    .UseConverter(x => x switch {
                        ParameterState state => DescribeParameter(state),
                        _ when ReferenceEquals(x, Back) => $"[grey]{Prompts.Arrow} back[/] [grey35](b)[/]",
                        _ => $"{(string)x} [grey35](r)[/]",
                    })
                    .AddChoices(choices),
                Prompts.BackOrRunKeys);
            if (shortcut == Prompts.QuitShortcut)
                _isQuitting = true;

            if (shortcut is Prompts.BackShortcut or Prompts.QuitShortcut)
                return null;
            if (shortcut == Prompts.RunShortcut) {
                if (!isRunnable)
                    continue;

                return args;
            }

            switch (choice) {
            case string s when ReferenceEquals(s, RunNow):
                return args;
            case string s when ReferenceEquals(s, Blocked):
                continue;
            case string:
                return null;
            case ParameterState state:
                if (state.Parameter.IsFlag) {
                    state.Value = state.Value is null ? "true" : null;
                    break;
                }

                var edit = PromptValue(state, args);
                if (_isQuitting)
                    return null;

                if (!edit.IsBack)
                    state.Value = edit.Value;

                break;
            }
        }
    }

    private async Task<(string? Error, CommandPlan? Plan)> Validate(string[] args)
    {
        // Re-runs the real command in validate-only mode: the parser binds and validates
        // the settings and the command builds its plan, but nothing executes.
        var console = AnsiConsole.Console;
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings {
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        Context.Mode = ExecutionMode.Validate;
        Context.LastPlan = null;
        try {
            var exitCode = await Dispatch(args).ConfigureAwait(false);
            return exitCode == 0
                ? (null, Context.LastPlan)
                : ("This command isn't runnable as configured.", null);
        }
        catch (Exception e) {
            return (e.Message, null);
        }
        finally {
            Context.Mode = ExecutionMode.Run;
            AnsiConsole.Console = console;
        }
    }

    private static string DescribeParameter(ParameterState state)
    {
        var value = state.Value ?? state.DefaultText;
        var marker = state.Value is not null ? "[green]*[/]" : " ";
        return $"{marker} [aqua]{state.Name}[/] = [yellow]{value.EscapeMarkup()}[/]"
            + $"  [grey]{state.Parameter.Description}[/]";
    }

    private (bool IsBack, string? Value) PromptValue(ParameterState state, string[] args)
    {
        WriteHeader(args, null, null);
        var title = $"[green]{state.Name}[/] [grey]{state.Parameter.Description}[/]";
        string? value;
        string? shortcut;
        if (state.EnumType is { } enumType)
            (value, shortcut) = Prompts.Show(
                new SelectionPrompt<string>()
                    .Title(title)
                    .PageSize(20)
                    .UseConverter(x => ReferenceEquals(x, Back) ? Prompts.BackLabel : x)
                    .AddChoices([Back, ..Enum.GetNames(enumType)]),
                Prompts.BackKeys);
        else {
            var prompt = new TextPrompt<string>($"{title}\n[green]{state.Name}[/]:").AllowEmpty();
            if (state.Value is { } current)
                prompt.DefaultValue(current);

            (value, shortcut) = Prompts.ShowText(prompt);
        }

        if (shortcut == Prompts.QuitShortcut)
            _isQuitting = true;

        if (shortcut is not null || ReferenceEquals(value, Back))
            return (true, null);

        return (false, value is { Length: > 0 } ? value : null);
    }

    private static string[] BuildArgs(string[] path, List<ParameterState> parameters)
    {
        var args = new List<string>(path);
        foreach (var state in parameters.OrderBy(x => (x.Parameter as ICommandArgument)?.Position ?? int.MaxValue)) {
            if (state.Value is not { } value)
                continue;

            switch (state.Parameter) {
            case ICommandArgument:
                args.Add(value);
                break;
            case ICommandOption option:
                args.Add("--" + option.LongNames.First());
                if (!state.Parameter.IsFlag)
                    args.Add(value);
                break;
            }
        }

        return [..args];
    }

    // Nested types

    private sealed record SelectedCommand(ICommandInfo Info, string[] Path);

    private sealed class ParameterState(ICommandParameter parameter)
    {
        public ICommandParameter Parameter { get; } = parameter;
        public string? Value { get; set; }
        public string Name => Parameter.GetLongName();
        public Type? EnumType => Parameter.GetEnumType();
        public string DefaultText => Parameter.IsFlag
            ? "off"
            : Parameter.GetDefaultValue()?.ToString() ?? (Parameter.IsRequired ? "<required>" : "<unset>");
    }
}
