using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI : IHasServices
{
    private IJSRuntime? _js;
    public IServiceProvider Services { get; }
    protected ILogger Log { get; private init; }
    private IJSRuntime JS => _js ??= Services.GetRequiredService<IJSRuntime>();

    public KeepAwakeUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Services = services;
    }

    public virtual ValueTask SetKeepAwake(bool value)
    {
        Log.LogDebug("SetKeepAwake({MustKeepAwake})", value);
        return JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake", value);
    }
}
