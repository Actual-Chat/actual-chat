namespace ActualChat.UI.Blazor.Services;

public sealed class RenderVars
{
    private readonly Dictionary<Symbol, RenderVar> _vars = new();

    public RenderVar<T> Get<T>(Symbol name, T @default = default!)
    {
        if (_vars.TryGetValue(name, out var renderVar))
            return (RenderVar<T>)renderVar;

        var newRenderVar = new RenderVar<T>(name, @default);
        _vars[name] = newRenderVar;
        return newRenderVar;
    }
}
