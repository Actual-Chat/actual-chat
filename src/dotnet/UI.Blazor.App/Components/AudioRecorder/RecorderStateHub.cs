using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

public class RecorderStateHub(UIHub hub)
{
    private static readonly string JSSetRecordingMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setRecording";

    private static readonly string JSSetConnectedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setConnected";

    private static readonly string JSSetSignalDetectedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setSignalDetected";

    private static readonly string JSSetVoiceActiveMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setVoiceActive";

    private static readonly string JSMicrophoneIsCapturedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.microphoneIsCaptured";

    private IJSRuntime JS => hub.JS;

    public ValueTask SetRecording(bool isRecording)
        => JS.InvokeVoidAsync(JSSetRecordingMethod, isRecording);

    public ValueTask SetConnected(bool isConnected)
        => JS.InvokeVoidAsync(JSSetConnectedMethod, isConnected);

    public ValueTask SetSignalDetected(bool isSignalDetected)
        => JS.InvokeVoidAsync(JSSetSignalDetectedMethod, isSignalDetected);

    public ValueTask SetVoiceActive(bool isVoiceActive)
        => JS.InvokeVoidAsync(JSSetVoiceActiveMethod, isVoiceActive);

    public ValueTask MicrophoneIsCaptured(double gain)
        => JS.InvokeVoidAsync(JSMicrophoneIsCapturedMethod, gain);
}
