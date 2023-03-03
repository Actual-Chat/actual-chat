namespace ActualChat.UI.Blazor.Services;

public sealed class RenderVars
{
    private readonly ConcurrentDictionary<string, IMutableState> _vars = new(StringComparer.Ordinal);

    private IStateFactory StateFactory { get; }

    public RenderVars(IStateFactory stateFactory)
        => StateFactory = stateFactory;

    public IMutableState<T> Get<T>(string name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(name));
        return (IMutableState<T>)_vars.GetOrAdd(
            name,
            static (name, self) => self.StateFactory.NewMutable(
                default(T),
                StateCategories.Get(typeof(RenderVars), name)
            ), this);
    }

    public IMutableState<T> Get<T>(string name, T @default)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(name));

        return (IMutableState<T>)_vars.GetOrAdd(name,
            static (_, arg) => arg.Self.StateFactory.NewMutable(arg.Default),
            (Self: this, Default: @default));
    }
}
