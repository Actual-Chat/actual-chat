namespace ActualChat;

public static class SymbolExt
{
    public static Symbol? NullIfEmpty(this Symbol source)
        => source.IsEmpty ? (Symbol?)null : source;

    public static Symbol RequireEmpty(this Symbol source, string name)
        => source.IsEmpty ? source : throw StandardError.Constraint($"{name} must be empty here.");

    public static Symbol RequireNonEmpty(this Symbol source, string name)
        => source.IsEmpty ? throw StandardError.Constraint($"{name} is required here.") : source;
}
