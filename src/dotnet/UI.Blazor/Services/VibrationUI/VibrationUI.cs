using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class VibrationUI
{
    private static readonly string JSVibrateMethod = $"{BlazorUICoreModule.ImportName}.VibrationUI.vibrate";
    private static readonly string JSVibrateAndWaitMethod = $"{BlazorUICoreModule.ImportName}.VibrationUI.vibrateAndWait";

    private IJSRuntime JS { get; }

    public VibrationUI(IServiceProvider services)
        => JS = services.JSRuntime();

    public ValueTask Vibrate(double? duration = null, CancellationToken cancellationToken = default)
        => Vibrate(duration is { } d ? TimeSpan.FromSeconds(d) : null, cancellationToken);
    public ValueTask Vibrate(TimeSpan? duration = null, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSVibrateMethod, cancellationToken, duration?.TotalMilliseconds);

    public Task VibrateAndWait(double duration, CancellationToken cancellationToken = default)
        => VibrateAndWait(TimeSpan.FromSeconds(duration), cancellationToken);
    public Task VibrateAndWait(TimeSpan? duration, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSVibrateAndWaitMethod, cancellationToken, duration?.TotalMilliseconds).AsTask();
}
