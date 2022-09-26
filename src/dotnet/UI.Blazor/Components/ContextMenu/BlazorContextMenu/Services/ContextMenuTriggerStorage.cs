namespace BlazorContextMenu.Services;

internal class ContextMenuTriggerStorage
{
    private readonly Dictionary<string, ContextMenuTrigger> _registeredTriggers = new (StringComparer.Ordinal);

    public void Register(string id, ContextMenuTrigger trigger)
        => _registeredTriggers[id] = trigger;

    public void Unregister(string id)
        => _registeredTriggers.Remove(id);

    public ContextMenuTrigger? GetTrigger(string id)
        => _registeredTriggers.TryGetValue(id, out var trigger) ? trigger : null;
}
