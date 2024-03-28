namespace ActualChat;

public static class StaticImports
{
    public static ILoggerFactory DefaultLoggerFactory { get; set; } = NullLoggerFactory.Instance;

    public static ILogger DefaultLogFor(Type type)
    {
        try {
            return DefaultLoggerFactory.CreateLogger(type);
        }
        catch {
            return NullLogger.Instance;
        }
    }

    public static ILogger DefaultLogFor<T>()
    {
        try {
            return DefaultLoggerFactory.CreateLogger<T>();
        }
        catch {
            return NullLogger.Instance;
        }
    }
}
