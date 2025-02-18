using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI(UIHub hub)
{
    private static readonly string JSSetKeepAwakeMethod = $"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake";

    protected UIHub Hub => hub;
    protected IJSRuntime JS => hub.JSRuntime();
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= hub.LogFor(GetType());

    public virtual ValueTask SetKeepAwake(bool mustKeepAwake)
    {
        Log.LogInformation("SetKeepAwake({MustKeepAwake})", mustKeepAwake);
        return JS.InvokeVoidAsync(JSSetKeepAwakeMethod, mustKeepAwake);
    }
}
