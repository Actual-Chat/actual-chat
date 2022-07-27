using System.ComponentModel.DataAnnotations;

namespace ActualChat;

public static class SymbolExt
{
    public static Symbol RequireNonEmpty(this Symbol source, string name)
        => source.IsEmpty ? throw StandardError.Constraint($"{name} is required here.") : source;
    public static Symbol RequireEmpty(this Symbol source, string name)
        => source.IsEmpty ? source : throw StandardError.Constraint($"{name} must be empty here.");
}
