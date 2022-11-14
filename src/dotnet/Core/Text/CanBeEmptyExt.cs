namespace ActualChat;

public static class CanBeEmptyExt
{
    public static Symbol? NullIfEmpty(this Symbol source)
        => source.IsEmpty ? (Symbol?)null : source;

    public static Symbol RequireNonEmpty(this Symbol source, string name)
        => source.IsEmpty ? throw StandardError.Constraint($"{name} is required here.") : source;
    public static T RequireNonEmpty<T>(this T source, string name)
        where T : ICanBeEmpty
        => source.IsEmpty ? throw StandardError.Constraint($"{name} is required here.") : source;
    public static T RequireNonEmpty<T>(this T source)
        where T : ICanBeEmpty
        => source.RequireNonEmpty(typeof(T).Name);

    public static Symbol RequireEmpty(this Symbol source, string name)
        => source.IsEmpty ? source : throw StandardError.Constraint($"{name} must be empty here.");
    public static T RequireEmpty<T>(this T source, string name)
        where T : ICanBeEmpty
        => source.IsEmpty ? source : throw StandardError.Constraint($"{name} must be empty here.");
    public static T RequireEmpty<T>(this T source)
        where T : ICanBeEmpty
        => source.RequireEmpty(typeof(T).Name);

}
