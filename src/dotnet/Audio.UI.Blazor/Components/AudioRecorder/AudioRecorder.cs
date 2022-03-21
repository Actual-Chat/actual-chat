using ActualChat.Audio.UI.Blazor.Module;

namespace ActualChat.Audio.UI.Blazor.Components;

public class AudioRecorderStatus
{
    private Symbol _chatId = "";

    [ComputeMethod]
    public virtual Task<Symbol> GetRecordingChat(CancellationToken cancellationToken = default)
        => Task.FromResult(_chatId);

    public void SetRecordingChat(Symbol chatId)
    {
        _chatId = chatId;
        using (Computed.Invalidate())
            _ = GetRecordingChat();
    }
}

public class AudioRecorder : IAudioRecorderBackend, IAsyncDisposable
{
    private readonly ILogger<AudioRecorder> _logger;
    private ILogger? DebugLog => DebugMode ? _logger : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;

    private readonly Session _session;
    private readonly IJSRuntime _js;
    private readonly AudioRecorderStatus _statusService;

    private IJSObjectReference? JSRef { get; set; }
    private DotNetObjectReference<IAudioRecorderBackend> BlazorRef { get; set; } = null!;
    private object? Recording { get; set; }
    private bool IsRecording => Recording != null!;
    public Task Initialization { get; }

    public AudioRecorder(
        ILogger<AudioRecorder> logger,
        Session session,
        IJSRuntime js,
        AudioRecorderStatus statusService)
    {
        _logger = logger;
        _session = session;
        _js = js;
        _statusService = statusService;
        Initialization = Init();

        async Task Init()
        {
            BlazorRef = DotNetObjectReference.Create<IAudioRecorderBackend>(this);
            JSRef = await _js.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                BlazorRef, _session.Id).ConfigureAwait(true);
        }
    }

    internal async Task StartRecording(string chatId) {
        if (IsRecording) return;
        DebugLog?.LogDebug(nameof(StartRecording));

        Recording = new object();
        if (JSRef != null)
            await JSRef.InvokeVoidAsync("startRecording", chatId).ConfigureAwait(false);

        _statusService.SetRecordingChat(chatId);
    }

    internal async Task StopRecording() {
        if (!IsRecording) return;
        DebugLog?.LogDebug(nameof(StopRecording));

        var recording = Recording;

        _ = Task.Delay(TimeSpan.FromSeconds(5))
            .ContinueWith(async _ => {
                if (Recording != recording)
                    return; // We don't want to stop the next recording here :)

                _logger.LogWarning(nameof(OnRecordingStopped) + " wasn't invoked on time by _js backend");
                await OnRecordingStopped().ConfigureAwait(true);
            }, TaskScheduler.Current);

        if (JSRef != null)
            await JSRef.InvokeVoidAsync("stopRecording").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        await StopRecording().ConfigureAwait(true);
        await JSRef.DisposeSilentlyAsync().ConfigureAwait(true);
        // ReSharper disable once ConstantConditionalAccessQualifier
        BlazorRef?.Dispose();
    }

    // JS backend callback handlers

    [JSInvokable]
    public void OnStartRecording() {
        if (!IsRecording) return;
        DebugLog?.LogDebug(nameof(OnStartRecording));
    }

    [JSInvokable]
    public Task OnRecordingStopped() {
        // Does the same as StopRecording; we assume here that recording
        // might be recognized as stopped by JS backend as well
        var recording = Recording;
        Recording = null!;
        if (recording == null) return Task.CompletedTask;
        DebugLog?.LogDebug(nameof(OnRecordingStopped));

        _statusService.SetRecordingChat(Symbol.Empty);
        return Task.CompletedTask;
    }
}
