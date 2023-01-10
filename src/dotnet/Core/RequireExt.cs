namespace ActualChat;

public static class RequireExt
{
    public static T? RequireNull<T>(this T? source)
        where T : class
        => source.RequireNull(typeof(T).GetName());
    public static T? RequireNull<T>(this T? source, string name)
        where T : class
        => source == null ? source : throw StandardError.Constraint($"This {name} already exists.");
}
