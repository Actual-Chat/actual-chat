namespace ActualChat.UI.Blazor.Services;

public sealed class RenderSlotRegistry
{
    private readonly ConcurrentDictionary<string, IMutableState<RenderFragment?>> _slots = new(StringComparer.Ordinal);
    private readonly IStateFactory _stateFactory;

    public RenderFragment? this[string name] {
        get => GetState(name).Value;
        set => GetState(name).Value = value;
    }

    public RenderSlotRegistry(IStateFactory stateFactory)
        => _stateFactory = stateFactory;

    public IMutableState<RenderFragment?> GetState(string name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));
        if (name.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(name));
        return _slots.GetOrAdd(name, static (_, self) => self._stateFactory.NewMutable<RenderFragment?>(), this);
    }
}
