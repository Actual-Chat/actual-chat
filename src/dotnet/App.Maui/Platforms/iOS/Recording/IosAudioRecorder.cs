
// IosAudioRecorder.cs
using ActualChat.App.Maui.Playback;
using ActualChat.UI.Blazor.App.Components;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Recording;

public class IosAudioRecorder
{
    private IAudioRecorderBackend _backend;
    private AVAudioRecorder _audioRecorder;
    private AVAudioEngine _audioEngine;
    private AVAudioPlayerNode _playerNode;
    private AVAudioFormat _audioFormat;
    private NSTimer _timer;
    private bool _isRecording;

    public async Task InitializeAsync(IAudioRecorderBackend backend, CancellationToken cancellationToken)
    {
        _backend = backend;
    }

    public async Task<bool> StartAsync(ChatId chatId, ChatEntryId? repliedChatEntryId, string sessionToken, CancellationToken cancellationToken)
    {
        IosAudioSessionHelper.ActivateRecordingAndBackgroundAudio();
        try
        {
            // Configure session for 16kHz and ~20ms I/O buffer when possible
            var session = AVAudioSession.SharedInstance();
            session.SetPreferredSampleRate(16000, out NSError? sessionErr1);
            session.SetPreferredIOBufferDuration(0.02, out NSError? sessionErr2);

            // Set up audio engine and tap input at 16kHz, mono, float32
            _audioEngine = new AVAudioEngine();
            var inputNode = _audioEngine.InputNode;
            _audioFormat = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, 16000, 1, true);

            // Install a tap to get 20ms buffers (320 frames @ 16kHz)
            inputNode.InstallTapOnBus(0, 320, _audioFormat, (buffer, when) =>
            {
                if (!_isRecording || buffer == null)
                    return;

                unsafe
                {
                    // AVAudioPCMBuffer.FloatChannelData -> float* for each channel
                    var dataPtr = buffer.FloatChannelData;
                    if (dataPtr == IntPtr.Zero)
                        return;

                    int frames = (int)buffer.FrameLength;
                    var channel0 = (float*)dataPtr;
                    var span = new ReadOnlySpan<float>(channel0, frames);

                    //TODO: Implement OnAudioDataReceived method
                    // await _backend.OnAudioDataReceived(span, CancellationToken.None);
                }
            });

            _audioEngine.Prepare();
            var started = _audioEngine.StartAndReturnError(out NSError? startError);
            if (!started)
            {
                Debug.WriteLine($"iOS Audio Engine start error: {startError?.LocalizedDescription}");
                try { inputNode.RemoveTapOnBus(0); } catch { /* ignored */ }
                _audioEngine.Dispose();
                _audioEngine = null;
                return false;
            }

            _isRecording = true;
            _backend?.OnRecordingStateChange(true, false, true, false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"iOS Audio Start Error: {ex.Message}");
            return false;
        }
    }


    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _timer?.Invalidate();
            _timer = null;

            // Stop engine-based recording
            if (_audioEngine != null)
            {
                try { _audioEngine.InputNode?.RemoveTapOnBus(0); } catch { /* ignored */ }
                try { if (_audioEngine.Running) _audioEngine.Stop(); } catch { /* ignored */ }

                // Detach and cleanup nodes
                foreach (var node in _audioEngine.AttachedNodes)
                {
                    try
                    {
                        if (node is AVAudioPlayerNode playerNode)
                            playerNode.Stop();
                        _audioEngine.DetachNode(node);
                    }
                    catch { /* ignored */ }
                }

                _audioEngine.Dispose();
                _audioEngine = null;
            }

            // Fallback recorder cleanup if it was used
            if (_audioRecorder?.Recording == true)
            {
                _audioRecorder.Stop();
            }
            _audioRecorder?.Dispose();
            _audioRecorder = null;

            _isRecording = false;
            _backend?.OnRecordingStateChange(false, false, true, false);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"iOS Audio Stop Error: {ex.Message}");
            return false;
        }
    }

    public ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        // iOS specific connection logic
        return ValueTask.CompletedTask;
    }

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        // iOS specific conversation signal
        return ValueTask.CompletedTask;
    }

    public async Task<string?> CheckPermissionAsync(CancellationToken cancellationToken)
    {
        var status = AVAudioApplication.SharedInstance.RecordPermission;
        return status switch
        {
            AVAudioApplicationRecordPermission.Granted => "granted",
            AVAudioApplicationRecordPermission.Denied => "denied",
            _ => "prompt"
        };
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        var permissionTcs = new TaskCompletionSource<bool>();
        AVAudioApplication.RequestRecordPermission(success =>  permissionTcs.SetResult(success));
        return await permissionTcs.Task;
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var session = AVAudioSession.SharedInstance();
        return new AudioRecorder.AudioDiagnosticsState
        {
            HasMicrophonePermission = session.RecordPermission == AVAudioSessionRecordPermission.Granted,

        };
    }
}
