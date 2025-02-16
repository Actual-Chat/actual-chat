using ActualChat.Diff.Handlers;

namespace ActualChat.Diff;

public class DiffEngine(
    IServiceProvider services,
    TypeMapper<IDiffHandler>? diffHandlerFinder = null
    ) : IHasServices
{
    private static volatile Lazy<DiffEngine> _defaultLazy;

    public static DiffEngine Default {
        get => _defaultLazy.Value;
        set => _defaultLazy = new Lazy<DiffEngine>(value);
    }

    private readonly ConcurrentDictionary<(Type SourceType, Type DiffType), IDiffHandler> _cachedHandlers = new();

    public IServiceProvider Services { get; } = services;
    public TypeMapper<IDiffHandler> DiffHandlerResolver { get; }
        = diffHandlerFinder
        ?? services.GetService<TypeMapper<IDiffHandler>>()
        ?? new TypeMapper<IDiffHandler>(DefaultTypeMapBuilder);

    static DiffEngine()
        => _defaultLazy = new Lazy<DiffEngine>(() => {
            var c = new ServiceCollection()
                .AddTypeMapper<IDiffHandler>(DefaultTypeMapBuilder)
                .AddSingleton<DiffEngine, DiffEngine>()
                .BuildServiceProvider();
            return c.GetRequiredService<DiffEngine>();
        });

    // GetHandler

    public IDiffHandler<T, TDiff> GetHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDiff>()
        => (IDiffHandler<T, TDiff>)GetHandler(typeof(T), typeof(TDiff));

    public IDiffHandler GetHandler(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type sourceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type diffType)
    {
        [UnconditionalSuppressMessage("Trimming", "IL2077", Justification = "Covered by DynamicallyAccessedMemberTypes.All above")]
        static IDiffHandler HandlerFactory((Type SourceType, Type DiffType) key, DiffEngine self) {
            var (tSource, tDiff) = key;
            return self.CreateHandler(tSource, tDiff);
        }

        return _cachedHandlers.GetOrAdd((sourceType, diffType), HandlerFactory, this);
    }

    // Diff & Patch

    public TDiff Diff<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TDiff>(T source, T target)
        => GetHandler<T, TDiff>().Diff(source, target);

    public T Patch<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TDiff>(T source, TDiff diff)
        => GetHandler<T, TDiff>().Patch(source, diff);

    public static void DefaultTypeMapBuilder(TypeMap<IDiffHandler> typeMap)
        => typeMap
            .Add(typeof(Nullable<>), typeof(NullableDiffHandler<>))
            .Add(typeof(Option<>), typeof(OptionDiffHandler<>))
            .Add(typeof(SetDiff<,>), typeof(SetDiffHandler<,>));

    // Protected methods

#pragma warning disable IL2067, IL2070, IL2072
    [UnconditionalSuppressMessage("Trimming", "IL2072:NotSatisfyDynamicallyAccessedMemberTypes.PublicConstructors", Justification = "T is marked with DynamicallyAccessedMembers.")]
    protected virtual IDiffHandler CreateHandler(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type sourceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]Type diffType)
    {
        var tHandler = DiffHandlerResolver.TryGet(diffType);
        if (tHandler == null && sourceType == diffType)
            tHandler = typeof(CloneDiffHandler<>).MakeGenericType(sourceType);
        if (tHandler == null && diffType.IsAssignableTo(typeof(RecordDiff)))
            tHandler = typeof(RecordDiffHandler<,>).MakeGenericType(sourceType, diffType);
        tHandler ??= typeof(MissingDiffHandler<,>).MakeGenericType(sourceType, diffType);
        return (IDiffHandler)Services.CreateInstance(tHandler);
    }
#pragma warning restore IL2067, IL2070, IL2072
}
