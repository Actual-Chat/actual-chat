namespace ActualChat;

public static class StaticImports
{
    private static readonly object _lock = new();
    private static readonly ConcurrentDictionary<object, ILogger> Cache = new();
    private static volatile ILoggerFactory _defaultLoggerFactory = NullLoggerFactory.Instance;

    public static ILoggerFactory DefaultLoggerFactory {
        get => _defaultLoggerFactory;
        set {
            lock (_lock) {
                if (ReferenceEquals(DefaultLoggerFactory, value))
                    return;

                _defaultLoggerFactory = value;
                Cache.Clear();
            }
        }
    }

    public static ILogger<T> DefaultLogFor<T>()
        => (ILogger<T>)Cache.GetOrAdd(typeof(T),
            static key => (ILogger)typeof(Logger<>).MakeGenericType((Type)key).CreateInstance(DefaultLoggerFactory));

    public static ILogger DefaultLogFor(Type type)
        => Cache.GetOrAdd(type.NonProxyType(),
            static key => DefaultLoggerFactory.CreateLogger((Type)key));

    public static ILogger DefaultLogFor(string category)
        => Cache.GetOrAdd(category,
            static key => DefaultLoggerFactory.CreateLogger((string)key));
}
