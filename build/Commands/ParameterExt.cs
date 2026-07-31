using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli.Help;

namespace Build.Commands;

internal static class ParameterExt
{
    public static string GetName(this ICommandParameter parameter)
        => parameter switch {
            ICommandArgument x => x.IsRequired ? $"<{x.Value}>" : $"[{x.Value}]",
            ICommandOption x => string.Join("|",
                [..x.ShortNames.Select(y => "-" + y), ..x.LongNames.Select(y => "--" + y)]),
            _ => "?",
        };

    public static string GetLongName(this ICommandParameter parameter)
        => parameter switch {
            ICommandArgument x => x.Value,
            ICommandOption x => "--" + x.LongNames.First(),
            _ => "?",
        };

    // Spectre stores the [DefaultValue] attribute itself rather than its value.
    public static object? GetDefaultValue(this ICommandParameter parameter)
        => parameter.DefaultValue is DefaultValueAttribute x ? x.Value : parameter.DefaultValue;

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Build-time tool, never trimmed.")]
    public static Type? GetEnumType(this ICommandParameter parameter)
    {
        // ICommandParameter doesn't expose the CLR type, so it's read off the concrete
        // parameter; null (free-text input) if Spectre ever renames the property.
        var type = parameter.GetType().GetProperty("ParameterType")?.GetValue(parameter) as Type;
        type = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
        return type?.IsEnum == true ? type : null;
    }
}
