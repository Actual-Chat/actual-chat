using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class TuneUI(IServiceProvider services)
{
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";
    private static readonly Dictionary<Tune, string> Tunes = new () {
        [Tune.RemindOfRecording] = "remind-of-recording",
    };

    private IJSRuntime JS { get; } = services.JSRuntime();

    public ValueTask Play(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayMethod, cancellationToken, tuneName);

    public ValueTask Play(Tune tune, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayMethod, cancellationToken, Tunes.GetValueOrDefault(tune));

    public ValueTask PlayAndWait(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, cancellationToken, tuneName);

    public ValueTask PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, cancellationToken, Tunes.GetValueOrDefault(tune));
}

public enum Tune
{
    RemindOfRecording,
}
