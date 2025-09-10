using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using ActualChat.App.Maui.Interop;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.App.Maui.Recording;

public class WindowsAudioRecorder
{
    private IAudioRecorderBackend _backend;
    private AudioGraph _audioGraph;
    private AudioDeviceInputNode _deviceInputNode;
    private AudioFrameOutputNode _frameOutputNode;
    private bool _isRecording;

    public async Task InitializeAsync(IAudioRecorderBackend backend, CancellationToken cancellationToken)
    {
        _backend = backend;
        await InitializeAudioGraph();
    }

    private async Task InitializeAudioGraph()
    {
        var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Communications)
        {
            EncodingProperties = AudioEncodingProperties.CreatePcm(16000, 1, 32), // 32-bit for float
            AudioRenderCategory = AudioRenderCategory.Communications,
            DesiredRenderDeviceAudioProcessing = AudioProcessing.Default,
        };
        settings.EncodingProperties.Subtype = MediaEncodingSubtypes.Float;

        var result = await AudioGraph.CreateAsync(settings);
        if (result.Status == AudioGraphCreationStatus.Success) {
            _audioGraph = result.Graph;

            // Create input node
            var inputResult = await _audioGraph.CreateDeviceInputNodeAsync(MediaCategory.Communications);
            if (inputResult.Status == AudioDeviceNodeCreationStatus.Success)
                _deviceInputNode = inputResult.DeviceInputNode;

            // Create output node for frame processing
            _frameOutputNode = _audioGraph.CreateFrameOutputNode();
            _audioGraph.QuantumProcessed += OnQuantumProcessed;
        }
    }

    public async Task<bool> StartAsync(ChatId chatId, ChatEntryId? repliedChatEntryId, string sessionToken, CancellationToken cancellationToken)
    {
        try
        {
            if (_deviceInputNode != null && _frameOutputNode != null)
            {
                _deviceInputNode.AddOutgoingConnection(_frameOutputNode);
                _audioGraph.Start();
                _isRecording = true;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows Audio Start Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _isRecording = false;

            _audioGraph?.Stop();
            _deviceInputNode?.RemoveOutgoingConnection(_frameOutputNode);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows Audio Stop Error: {ex.Message}");
            return false;
        }
    }

    private async void OnQuantumProcessed(AudioGraph sender, object args)
    {
        if (!_isRecording)
            return;

        try
        {
            using var frame = _frameOutputNode.GetFrame();
            using var audioBuffer = frame.LockBuffer(AudioBufferAccessMode.Read);
            using var bufferReference = audioBuffer.CreateReference();

            unsafe {
                // ReSharper disable once SuspiciousTypeConversion.Global
                ((IMemoryBufferByteAccess)bufferReference).GetBuffer(out byte* bufferPtr, out uint capacity);

                if (capacity >= sizeof(float)) {
                    int floatCount = (int)(capacity / sizeof(float));
                    var span = new ReadOnlySpan<float>((float*)bufferPtr, floatCount);

                    //TODO: Implement OnAudioDataReceived method
                    // await _backend.OnAudioDataReceived(span, CancellationToken.None);

                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows Audio Processing Error: {ex.Message}");
        }
    }

    public ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        // Windows specific connection logic
        return ValueTask.CompletedTask;
    }

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        // Windows specific conversation signal
        return ValueTask.CompletedTask;
    }

    public async Task<string?> CheckPermissionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio
            };

            await mediaCapture.InitializeAsync(settings);
            mediaCapture.Dispose();

            return "granted";
        }
        catch (UnauthorizedAccessException)
        {
            return "denied";
        }
        catch
        {
            return "prompt";
        }
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        // Windows handles permissions differently - typically through app manifest
        var status = await CheckPermissionAsync(cancellationToken);
        return status == "granted";
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var hasPermission = await CheckPermissionAsync(cancellationToken) == "granted";

        return new AudioRecorder.AudioDiagnosticsState
        {
            HasMicrophonePermission = hasPermission,
            IsAudioContextRunning = _audioGraph != null,

            // IsAvailable = hasPermission && _audioGraph != null,
            // SampleRate = 16000,
            // Channels = 1,
            // IsRecording = _isRecording
        };
    }
}
