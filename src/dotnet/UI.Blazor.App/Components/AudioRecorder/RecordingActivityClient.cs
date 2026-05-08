
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

    private readonly UIHub _hub;
    // Coalesce audio power submissions: the JS bridge can stall under GC /
    // long-task pressure, and power is a continuous visualization signal —
    // intermediate values are safe to drop, only the latest matters.
    private readonly Coalescer<double> _audioPowerCoalescer;

    private IJSRuntime JS => _hub.JS;

    public RecordingActivityClient(UIHub hub)
    {
        _hub = hub;
        _audioPowerCoalescer = Coalescer.New<double>(SetAudioPowerInternal);
    }

    public ValueTask SetRecording(bool isRecording)
        => JS.InvokeVoidAsync(JSSetRecordingMethod, isRecording);

    public ValueTask SetVoiceActive(bool isVoiceActive)
        => JS.InvokeVoidAsync(JSSetVoiceActiveMethod, isVoiceActive);

    public void SetAudioPower(double power)
        => _audioPowerCoalescer.Submit(power);

    private ValueTask SetAudioPowerInternal(double power)
        => JS.InvokeVoidAsync(JSSetAudioPowerMethod, power);
}
