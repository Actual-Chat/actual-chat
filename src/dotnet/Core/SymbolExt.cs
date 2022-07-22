using System.ComponentModel.DataAnnotations;

namespace ActualChat;

public static class SymbolExt
{
    public static Symbol RequireNonEmpty(this Symbol source, string name)
        => source.IsEmpty ? throw new ValidationException($"{name} is required here.") : source;
    public static Symbol RequireEmpty(this Symbol source, string name)
        => source.IsEmpty ? source : throw new ValidationException($"{name} must be empty here.");
}
