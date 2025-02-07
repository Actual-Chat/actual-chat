using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI(UIHub hub)
{
    private static readonly string JSSetKeepAwakeMethod = $"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake";

    protected IJSRuntime JS => hub.JSRuntime();
    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= hub.LogFor(GetType());

    public virtual ValueTask SetKeepAwake(bool value)
    {
        Log.LogInformation("SetKeepAwake({MustKeepAwake})", value);
        return JS.InvokeVoidAsync(JSSetKeepAwakeMethod, value);
    }
}
