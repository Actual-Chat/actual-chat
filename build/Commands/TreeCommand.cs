using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Help;

namespace Build.Commands;

/// <summary>
/// Prints the whole command tree at once - the same model
/// <see cref="CommandBrowser"/> walks interactively.
/// </summary>
public sealed class TreeCommand : Command<TreeCommand.Settings>
{
    public static ICommandModel? Model { get; set; }

    protected override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        if (Model is not { } model)
            throw new WithoutStackException("The command model isn't available.");

        var tree = new Tree($"[aqua]{model.ApplicationName}[/]");
        foreach (var command in model.Commands.Where(x => !x.IsHidden && !x.IsDefaultCommand))
            AddCommand(tree.AddNode(Label(command)), command, settings.HasOptions);

        AnsiConsole.Write(tree);
        return 0;
    }

    // Private methods

    private static void AddCommand(TreeNode node, ICommandInfo command, bool hasOptions)
    {
        if (hasOptions)
            foreach (var parameter in command.Parameters.Where(x => !x.IsHidden))
                node.AddNode(Label(parameter));

        foreach (var child in command.Commands.Where(x => !x.IsHidden && !x.IsDefaultCommand))
            AddCommand(node.AddNode(Label(child)), child, hasOptions);
    }

    private static string Label(ICommandInfo command)
        => $"[aqua]{command.Name}[/]{(command.IsBranch ? " [grey]...[/]" : "")}  [grey]{command.Description}[/]";

    private static string Label(ICommandParameter parameter)
    {
        var name = parameter.GetName().EscapeMarkup();
        var choices = parameter.GetEnumType() is { } enumType
            ? $" [silver]({string.Join('|', Enum.GetNames(enumType))})[/]"
            : "";
        var defaultValue = parameter.GetDefaultValue();
        var value = defaultValue is null or false ? "" : $" [yellow]= {defaultValue}[/]";
        return $"[silver]{name}[/]{choices}{value}  [grey]{parameter.Description}[/]";
    }

    // Nested types

    public sealed class Settings : CommandSettings
    {
        [CommandOption("-o|--options")]
        [Description("Also show arguments and options")]
        public bool HasOptions { get; init; }
    }
}
