using ActualChat.Audio.UI.Blazor.Module;

namespace ActualChat.Audio.UI.Blazor.Pages;

#pragma warning disable CS0162 // for if (false) { logging }
#pragma warning disable MA0040
#pragma warning disable CA1822

public partial class AudioRecorderTestPage : ComponentBase, IDisposable
{
    [Inject] private ILogger<AudioRecorderTestPage> Log { get; init; } = null!;
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private Session Session { get; init; } = null!;

    private CancellationTokenSource? _cts;
    private CancellationTokenRegistration _registration;
    private IJSObjectReference? _jsRef;
    private ElementReference _recordsRef;
    private int _recordNumber;

    private ChatId ChatId { get; } = new("the-actual-one");
    private bool DebugMode { get; } = true;
    private bool IsRecording { get; set; }

    public async Task ToggleRecording()
    {
        if (!IsRecording) {
            Log.LogInformation("Recording was started");
            _cts = new CancellationTokenSource();
            var blazorRef = DotNetObjectReference.Create(this);
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioRecorderTestPage.createObj",
                _cts.Token, blazorRef, DebugMode, _recordsRef, _recordNumber++
                );
#pragma warning disable VSTHRD101, MA0040, MA0147
            // ReSharper disable once AsyncVoidLambda
            _registration = _cts.Token.Register(async () => {
                Log.LogInformation("Recording was cancelled");
                try {
                    await _jsRef.InvokeVoidAsync("stopRecording");
                    await _jsRef.DisposeSilentlyAsync();
                    if (_registration != default) {
                        await _registration.DisposeAsync();
                    }
                    blazorRef.Dispose();
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Unhandled exception during cancelling recording");
                }
                finally {
                    IsRecording = false;
                    _registration = default;
                    _jsRef = null;
                    StateHasChanged();
                }
            });
            await _jsRef.InvokeVoidAsync("startRecording", ChatId, ChatEntryId.None);
            IsRecording = true;
        }
        else {
            _cts.CancelAndDisposeSilently();
            _cts = null;
            await _registration.DisposeAsync();
            IsRecording = false;
        }
    }

    [JSInvokable]
    public void OnStartRecording()
    { }

    [JSInvokable]
    public Task OnAudioData(byte[] _) => Task.CompletedTask;

    [JSInvokable]
    public void OnRecordingStopped()
        => StateHasChanged();

    public void Dispose()
    {
        if (_registration != default) {
            _registration.Dispose();
            _registration = default;
        }
        _cts?.CancelAndDisposeSilently();
        GC.SuppressFinalize(this);
    }
}
