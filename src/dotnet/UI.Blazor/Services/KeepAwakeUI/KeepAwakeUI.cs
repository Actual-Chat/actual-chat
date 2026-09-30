using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Prevents the device screen from sleeping during active audio playback or recording,
/// and turns it off at the ear during an earpiece call.
/// </summary>
public class KeepAwakeUI(UIHub hub)
{
    private static readonly string JSSetKeepAwakeMethod = $"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake";

    protected UIHub Hub => hub;
    protected IJSRuntime JS => hub.JS;
    protected ILogger Log => field ??= hub.LogFor(GetType());

    public virtual ValueTask SetKeepAwake(bool mustKeepAwake)
    {
        Log.LogInformation("SetKeepAwake({MustKeepAwake})", mustKeepAwake);
        return JS.InvokeVoidAsync(JSSetKeepAwakeMethod, mustKeepAwake);
    }

    public virtual ValueTask SetScreenOffAtEar(bool isEnabled)
        // Lets the proximity sensor blank the screen and block touches, as in a phone call.
        => default;
}
