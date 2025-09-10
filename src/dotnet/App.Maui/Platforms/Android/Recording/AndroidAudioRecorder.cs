
// AndroidAudioRecorder.cs
using ActualChat.UI.Blazor.App.Components;
using Android.Content.PM;
using Android.Media;
using AndroidAudioSource = Android.Media.AudioSource;

namespace ActualChat.App.Maui.Recording;

public class AndroidAudioRecorder
{
    private IAudioRecorderBackend _backend;
    private AudioRecord _audioRecord;
    private Thread _recordingThread;
    private bool _isRecording;
    private CancellationTokenSource _cancellationTokenSource;
    private const int SampleRate = 16000;
    private const int ChannelConfig = (int)ChannelIn.Mono;
    private const Android.Media.Encoding AudioFormat = Android.Media.Encoding.PcmFloat;

    public async Task InitializeAsync(IAudioRecorderBackend backend, CancellationToken cancellationToken)
    {
        _backend = backend;
        await Task.CompletedTask;
    }

    public async Task<bool> StartAsync(ChatId chatId, ChatEntryId? repliedChatEntryId, string sessionToken, CancellationToken cancellationToken)
    {
        try
        {
            var bufferSize = AudioRecord.GetMinBufferSize(SampleRate,
                (ChannelIn)ChannelConfig, AudioFormat);
            bufferSize = Math.Max(bufferSize, 4096 * 4); // Larger buffer for float

            _audioRecord = new AudioRecord(
                AndroidAudioSource.VoiceCommunication,
                SampleRate,
                (ChannelIn)ChannelConfig,
                AudioFormat,
                bufferSize);

            _audioRecord.StartRecording();
            _isRecording = true;

            _cancellationTokenSource = new CancellationTokenSource();
            _recordingThread = new Thread(() => RecordAudio(cancellationToken));
            _recordingThread.Start();

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Android Audio Start Error: {ex.Message}");
            return false;
        }
    }

    private async void RecordAudio(CancellationToken cancellationToken)
    {
        // Buffer for 20ms of audio at 16kHz (320 samples)
        var buffer = new float[320];

        while (_isRecording && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var samplesRead = _audioRecord.Read(buffer, 0, buffer.Length, 0);

                if (samplesRead > 0)
                {
                    var data = new ReadOnlySpan<float>(buffer, 0, samplesRead);
                    //TODO: Implement OnAudioDataReceived method
                    // await _backend.OnAudioDataReceived(data, cancellationToken);
                }
                else if (samplesRead < 0)
                {
                    // Error occurred
                    break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Android Audio Processing Error: {ex.Message}");
                break;
            }
        }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();

        _isRecording = false;

        _audioRecord?.Stop();
        _audioRecord?.Release();
        _audioRecord = null;

        if (_recordingThread?.IsAlive == true)
        {
            _recordingThread.Join(1000); // Wait up to 1 second
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        return true;
    }

    public ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        // Android specific connection logic
        return ValueTask.CompletedTask;
    }

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        // Android specific conversation signal
        return ValueTask.CompletedTask;
    }

    public async Task<string?> CheckPermissionAsync(CancellationToken cancellationToken)
    {
        var permission = AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
            Platform.CurrentActivity,
            Android.Manifest.Permission.RecordAudio);

        return permission == Permission.Granted ? "granted" : "denied";
    }

    public async Task<bool> RequestPermissionAsync(CancellationToken cancellationToken)
    {
        // This should be called from UI thread
        var taskCompletionSource = new TaskCompletionSource<bool>();

        Platform.CurrentActivity.RequestPermissions(new[]
        {
            Android.Manifest.Permission.RecordAudio
        }, 1001);

        // In a real implementation, you'd handle the permission result callback
        // and complete the taskCompletionSource accordingly
        return await taskCompletionSource.Task;
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var hasPermission = await CheckPermissionAsync(cancellationToken) == "granted";
        var hasMicrophone = Platform.CurrentActivity.PackageManager.HasSystemFeature(PackageManager.FeatureMicrophone);

        return new AudioRecorder.AudioDiagnosticsState
        {
            HasMicrophonePermission = hasPermission,
            // Has = hasMicrophone
        };
    }
}
