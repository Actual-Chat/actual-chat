
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

// Thin JSI shim that pushes UI-activity state from C# (MAUI) into the JS-side
// RecordingActivity store consumed by Lit recording-SVG components.
// Web doesn't use this — opus-media-recorder.ts updates RecordingActivity directly.
public class RecordingActivityClient
{
    private static readonly string JSSetRecordingMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setRecording";
    private static readonly string JSSetVoiceActiveMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setVoiceActive";
    private static readonly string JSSetAudioPowerMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setAudioPower";

    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(1);

    private readonly Lock _pushLock = new();
    private Task _pushChain = Task.CompletedTask;
    private readonly UIHub _hub;
    // Coalesce audio power submissions: the JS bridge can stall under GC /
    // long-task pressure, and power is a continuous visualization signal —
    // intermediate values are safe to drop, only the latest matters.
    private readonly Coalescer<double> _audioPowerCoalescer;

    private IJSRuntime JS => _hub.JS;
    private ILogger Log => field ??= _hub.LogFor(GetType());

    public RecordingActivityClient(UIHub hub)
    {
        _hub = hub;
        _audioPowerCoalescer = Coalescer.New<double>(SetAudioPowerInternal);
    }

    public ValueTask SetRecording(bool isRecording)
        => Push(JSSetRecordingMethod, isRecording);

    public ValueTask SetVoiceActive(bool isVoiceActive)
        => Push(JSSetVoiceActiveMethod, isVoiceActive);

    public void SetAudioPower(double power)
        => _audioPowerCoalescer.Submit(power);

    private ValueTask SetAudioPowerInternal(double power)
        => Push(JSSetAudioPowerMethod, power);

    private ValueTask Push(string method, object value)
    {
        // Detached, so a JS call that never completes can't hang the recorder start path it sits
        // on; chained rather than fired loose so the UI still sees the pushes in order.
        lock (_pushLock)
            _pushChain = _pushChain.ContinueWith(
                _ => PushUnsafe(method, value).AsTask(),
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap();
        return default;
    }

    private async ValueTask PushUnsafe(string method, object value)
    {
        using var cts = new CancellationTokenSource(PushTimeout);
        try {
            await JS.InvokeVoidAsync(method, cts.Token, value).ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { }
        catch (OperationCanceledException) {
            Log.LogWarning("Push: {Method} didn't complete in {Timeout}", method, PushTimeout);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Push: {Method} failed", method);
        }
    }
}
