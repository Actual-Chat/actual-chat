namespace ActualChat;

public static class CanBeNoneExt
{
    public static T RequireNone<T>(this T source)
        where T : ICanBeNone
        => source.RequireNone(typeof(T).GetName());
    public static T RequireNone<T>(this T source, string name)
        where T : ICanBeNone
        => source.IsNone ? source : throw StandardError.Constraint($"{name} must be None here.");

    public static T Require<T>(this T source)
        where T : ICanBeNone
        => source.Require(typeof(T).GetName());
    public static T Require<T>(this T source, string name)
        where T : ICanBeNone
        => source.IsNone ? throw StandardError.Constraint($"{name} is required here.") : source;
}
