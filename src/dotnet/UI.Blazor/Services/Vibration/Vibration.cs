using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class Vibration
{
    private IJSRuntime JS { get; }

    public Vibration(IJSRuntime js)
        => JS = js;

    public ValueTask Vibrate(TimeSpan? duration)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.Vibration.vibrate",
            duration?.TotalMilliseconds);
}
