using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

// Thin JSI shim that pushes UI-activity state from C# (MAUI) into the JS-side
// RecordingActivity store consumed by Lit recording-SVG components.
// Web doesn't use this — opus-media-recorder.ts updates RecordingActivity directly.
public class RecordingActivityClient(UIHub hub)
{
    private static readonly string JSSetRecordingMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setRecording";

    private static readonly string JSSetVoiceActiveMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setVoiceActive";

    private static readonly string JSSetAudioPowerMethod =
        $"{BlazorUIAppModule.ImportName}.RecordingActivity.setAudioPower";

    private IJSRuntime JS => hub.JS;

    public ValueTask SetRecording(bool isRecording)
        => JS.InvokeVoidAsync(JSSetRecordingMethod, isRecording);

    public ValueTask SetVoiceActive(bool isVoiceActive)
        => JS.InvokeVoidAsync(JSSetVoiceActiveMethod, isVoiceActive);

    public ValueTask SetAudioPower(double power)
        => JS.InvokeVoidAsync(JSSetAudioPowerMethod, power);
}
