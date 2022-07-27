namespace ActualChat;

public static class BoolExt
{
    public static bool RequireTrue(this bool source, string message)
        => !source ? throw StandardError.Constraint(message) : source;
    public static bool RequireFalse(this bool source, string message)
        => source ? throw StandardError.Constraint(message) : source;
}
