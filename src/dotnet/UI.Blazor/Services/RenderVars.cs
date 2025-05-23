namespace ActualChat.UI.Blazor.Services;

public sealed class RenderVars
{
    private readonly Dictionary<string, RenderVar> _vars = new(StringComparer.Ordinal);

    public RenderVar<T> Get<T>(string name, Func<T>? factory = null)
    {
        if (_vars.TryGetValue(name, out var renderVar))
            return (RenderVar<T>)renderVar;

        var value = factory is not null
            ? factory.Invoke()
            : default!;
        var newRenderVar = new RenderVar<T>(name, value);
        _vars[name] = newRenderVar;
        return newRenderVar;
    }
}
