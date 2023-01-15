using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class Vibration
{
    private IJSRuntime JS { get; }
    private MomentClockSet Clocks { get; }

    public Vibration(IServiceProvider services)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        Clocks = services.GetRequiredService<MomentClockSet>();
    }

    public ValueTask Vibrate(double? duration = null)
        => Vibrate(duration is { } d ? TimeSpan.FromSeconds(d) : null);
    public ValueTask Vibrate(TimeSpan? duration = null)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.Vibration.vibrate",
            duration?.TotalMilliseconds);

    public Task VibrateAndWait(double duration)
        => VibrateAndWait(TimeSpan.FromSeconds(duration));
    public Task VibrateAndWait(TimeSpan? duration)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.Vibration.vibrateAndWait",
            duration?.TotalMilliseconds).AsTask();

    public ValueTask Play(string tuneName)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.Vibration.play",
            tuneName);
    public ValueTask PlayAndWait(string tuneName)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.Vibration.playAndWait",
            tuneName);
}
