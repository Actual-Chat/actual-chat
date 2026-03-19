using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Video;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiVideoRecorderEngine : IVideoRecorderEngine
{
    private CancellationTokenSource? _recordingCts;
    private Task? _sendTask;
    private Task? _qualityTask;
    private readonly AppUIHub _hub;

    private INativeVideoCapture VideoCapture => field ??= _hub.Services.GetRequiredService<INativeVideoCapture>();
    private IStreamClient StreamClient => field ??= _hub.Services.GetRequiredService<IStreamClient>();
    private CameraPermissionHandler CameraPermissionHandler => field ??= _hub.Services.GetRequiredService<CameraPermissionHandler>();
    private ChatVideoUI ChatVideoUI => _hub.ChatVideoUI;
    private ILogger Log => field ??= _hub.LogFor<MauiVideoRecorderEngine>();

    public bool IsNativeCapture => true;

    public MauiVideoRecorderEngine(AppUIHub hub)
        => _hub = hub;

    public async Task<bool> Start(ChatId chatId, CancellationToken ct = default)
    {
        Log.LogInformation("Start: ChatId={ChatId}", chatId);

        // Stop any existing recording
        await Stop(ct).ConfigureAwait(false);

        // Check camera permission
        var permissionGranted = await CameraPermissionHandler.CheckOrRequest(ct).ConfigureAwait(false);
        if (!permissionGranted) {
            Log.LogWarning("Start: Camera permission denied");
            return false;
        }

        var recordingCts = new CancellationTokenSource();
        _recordingCts = recordingCts;
        var recordingCt = recordingCts.Token;

        try {
            // Start native capture
            var config = new VideoCaptureConfig();
            var frameStream = await VideoCapture.StartCapture(config, recordingCt).ConfigureAwait(false);

            // Show preview
            MainThread.BeginInvokeOnMainThread(() => VideoCapture.ShowPreview());

            // Start sending frames via RPC
            var session = _hub.Session;
            var serverClock = _hub.Clocks.ServerClock;
            double clientStartOffset = serverClock.Now.EpochOffset.TotalSeconds;
            var format = new VideoFormat {
                Codec = "hvc1",
                Width = config.Width,
                Height = config.Height,
            };

            _sendTask = BackgroundTask.Run(
                () => StreamClient.PushVideo(
                    session,
                    chatId.Value,
                    clientStartOffset,
                    format,
                    frameStream,
                    recordingCt),
                Log, "Failed to push video stream", recordingCt);

            // Notify UI
            ChatVideoUI.OnRecordingStarted(chatId);

            // Subscribe to quality requests
            _qualityTask = BackgroundTask.Run(
                () => SubscribeToQualityRequests(chatId, recordingCt),
                Log, "Quality subscription failed", recordingCt);

            return true;
        }
        catch (Exception e) {
            Log.LogError(e, "Start: Failed to start video capture");
            await Stop(ct).ConfigureAwait(false);
            return false;
        }
    }

    public async Task<bool> Stop(CancellationToken ct = default)
    {
        Log.LogInformation("Stop");

        var recordingCts = Interlocked.Exchange(ref _recordingCts, null);
        recordingCts.CancelAndDisposeSilently();

        // Wait for send task
        var sendTask = Interlocked.Exchange(ref _sendTask, null);
        if (sendTask != null)
            try {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) {
                Log.LogError(e, "Stop: Error awaiting send task");
            }

        // Wait for quality task
        var qualityTask = Interlocked.Exchange(ref _qualityTask, null);
        if (qualityTask != null)
            try {
                await qualityTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) {
                Log.LogWarning(e, "Stop: Error awaiting quality task");
            }

        // Stop capture (also hides preview)
        await VideoCapture.StopCapture().ConfigureAwait(false);

        // Notify UI
        ChatVideoUI.OnRecordingStopped();

        return true;
    }

    private async Task SubscribeToQualityRequests(ChatId chatId, CancellationToken ct)
    {
        try {
            // Wait for our own stream to appear
            StreamId? ownStreamId = null;
            var ownAuthor = await _hub.Authors.GetOwn(_hub.Session, chatId, ct).ConfigureAwait(false);
            if (ownAuthor == null)
                return;

            var liveVideoStreams = _hub.Services.GetRequiredService<ILiveVideoStreams>();
            for (var i = 0; i < 30; i++) {
                var streams = await liveVideoStreams
                    .ListActiveStreams(_hub.Session, chatId, ct)
                    .ConfigureAwait(false);
                var ownStream = streams.FirstOrDefault(s => s.AuthorId == ownAuthor.Id);
                if (ownStream != default) {
                    ownStreamId = ownStream.StreamId;
                    break;
                }
                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            if (ownStreamId == null) {
                Log.LogWarning("SubscribeToQualityRequests: own stream not found");
                return;
            }

            Log.LogInformation("SubscribeToQualityRequests: subscribing for stream #{StreamId}", ownStreamId);

            var qualityStream = await liveVideoStreams
                .ObserveStreamQualityRequests(_hub.Session, ownStreamId, ct)
                .ConfigureAwait(false);

            await foreach (var preset in qualityStream.WithCancellation(ct).ConfigureAwait(false)) {
                Log.LogInformation("SubscribeToQualityRequests: preset {Level} ({Width}x{Height} @ {Bitrate}bps)",
                    preset.Level, preset.Width, preset.Height, preset.Bitrate);

                VideoCapture.Reconfigure(preset.Width, preset.Height, preset.Bitrate);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) {
            Log.LogWarning(e, "SubscribeToQualityRequests failed");
        }
    }
}
