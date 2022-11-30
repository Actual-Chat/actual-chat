using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class Vibration
{
    private IJSRuntime JS { get; }

    public Vibration(IJSRuntime js)
        => JS = js;

    public ValueTask Vibrate()
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.Vibration.vibrate");
}
