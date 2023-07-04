using ActualChat.Diff.Handlers;

namespace ActualChat.Diff;

public class DiffEngine : IHasServices
{
    private static volatile Lazy<DiffEngine> _defaultLazy;

    public static DiffEngine Default {
        get => _defaultLazy.Value;
        set => _defaultLazy = new Lazy<DiffEngine>(value);
    }

    private readonly ConcurrentDictionary<(Type SourceType, Type DiffType), IDiffHandler> _cachedHandlers = new();

    public IServiceProvider Services { get; }
    public TypeMapper<IDiffHandler> DiffHandlerResolver { get; }

    static DiffEngine()
        => _defaultLazy = new Lazy<DiffEngine>(() => {
            var services = new ServiceCollection()
                .AddTypeMapper<IDiffHandler>(DefaultTypeMapBuilder)
                .AddSingleton<DiffEngine, DiffEngine>()
                .BuildServiceProvider();
            return services.GetRequiredService<DiffEngine>();
        });

    public DiffEngine(IServiceProvider services, TypeMapper<IDiffHandler>? diffHandlerFinder = null)
    {
        Services = services;
        DiffHandlerResolver = diffHandlerFinder
            ?? services.GetService<TypeMapper<IDiffHandler>>()
            ?? new TypeMap<IDiffHandler>(DefaultTypeMapBuilder);
    }

    // GetHandler

    public IDiffHandler<T, TDiff> GetHandler<T, TDiff>()
        => (IDiffHandler<T, TDiff>) GetHandler(typeof(T), typeof(TDiff));

    public IDiffHandler GetHandler(Type sourceType, Type diffType)
        => _cachedHandlers.GetOrAdd((sourceType, diffType), static (key, self) => {
            var (tSource, tDiff) = key;
            return self.CreateHandler(tSource, tDiff);
        }, this);

    // Diff & Patch

    public TDiff Diff<T, TDiff>(T source, T target)
        => GetHandler<T, TDiff>().Diff(source, target);

    public T Patch<T, TDiff>(T source, TDiff diff)
        => GetHandler<T, TDiff>().Patch(source, diff);

    public static void DefaultTypeMapBuilder(Dictionary<Type, Type> map)
        => map
            .AddGeneric(typeof(Nullable<>), typeof(NullableDiffHandler<>))
            .AddGeneric(typeof(Option<>), typeof(OptionDiffHandler<>))
            .AddGeneric(typeof(SetDiff<,>), typeof(SetDiffHandler<,>));

    // Protected methods

#pragma warning disable IL2070
    protected virtual IDiffHandler CreateHandler(Type sourceType, Type diffType)
    {
        var tHandler = DiffHandlerResolver.TryGet(diffType);
        if (tHandler == null && sourceType == diffType)
            tHandler = typeof(CloneDiffHandler<>).MakeGenericType(sourceType);
        if (tHandler == null && diffType.IsAssignableTo(typeof(RecordDiff)))
            tHandler = typeof(RecordDiffHandler<,>).MakeGenericType(sourceType, diffType);
        tHandler ??= typeof(MissingDiffHandler<,>).MakeGenericType(sourceType, diffType);
        return (IDiffHandler) Services.Activate(tHandler);
    }
#pragma warning restore IL2070
}
