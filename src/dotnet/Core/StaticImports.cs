namespace ActualChat;

public static class StaticImports
{
    public static ILogger DefaultLog { get; set; } = NullLogger.Instance;
}
