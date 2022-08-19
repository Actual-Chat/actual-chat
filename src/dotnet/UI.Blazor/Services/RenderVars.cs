namespace ActualChat.UI.Blazor.Services;

public sealed class RenderVars
{
    private readonly ConcurrentDictionary<string, IMutableState> _vars = new(StringComparer.Ordinal);
    private readonly IStateFactory _stateFactory;

    public RenderVars(IStateFactory stateFactory)
        => _stateFactory = stateFactory;

    public IMutableState<T> Get<T>(string name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(name));
        return (IMutableState<T>)_vars.GetOrAdd(name, static (_, self) => self._stateFactory.NewMutable<T>(), this);
    }

    public IMutableState<T> Get<T>(string name, T @default)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(name));
        return (IMutableState<T>)_vars.GetOrAdd(name,
            static (_, x) => x.Self._stateFactory.NewMutable(x.Default),
            (Self: this, Default: @default));
    }
}
