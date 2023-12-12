using System.Text.RegularExpressions;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed partial class AudioInitializer(UIHub hub)
    : ScopedWorkerBase<UIHub>(hub), IAudioInfoBackend
{
    private static readonly string JSInitMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.init";
    private static readonly string JSUpdateBackgroundStateMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.setBackgroundState";
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(5);

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IosDeviceRegexFactory();
    private static readonly Regex IosDeviceRegex = IosDeviceRegexFactory();

    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;
    private AppActivity? _appActivity;
    private IJSRuntime? _js;
    private UrlMapper? _urlMapper;

    private IJSRuntime JS => _js ??= Services.JSRuntime();
    private UrlMapper UrlMapper => _urlMapper ??= Services.UrlMapper();
    private AppActivity AppActivity => _appActivity ??= Services.GetRequiredService<AppActivity>();

    public Task WhenInitialized => _whenInitializedSource.Task;

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("AudioInitializer: started");
            var retryDelays = RetryDelaySeq.Exp(0.1, 3);
            var whenInitialized = AsyncChainExt.From(Initialize, $"{nameof(AudioInitializer)}.{nameof(Initialize)}")
                .Log(LogLevel.Debug, Log)
                .Retry(retryDelays, 5, Log)
                .Run(cancellationToken);
            await _whenInitializedSource.TrySetFromTaskAsync(whenInitialized, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("AudioInitializer: initialized with status {Status}", whenInitialized.Status);

            await AsyncChainExt.From(UpdateBackgroundState, $"{nameof(AudioInitializer)}.{nameof(UpdateBackgroundState)}")
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
                .Run(cancellationToken)
                .ConfigureAwait(false);
        }
        finally {
            _backendRef.DisposeSilently();
        }
    }

    private async Task Initialize(CancellationToken cancellationToken)
    {
        var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
        await JS
            .InvokeVoidAsync(JSInitMethod, CancellationToken.None, backendRef, UrlMapper.BaseUrl, CanUseNNVad())
            .AsTask().WaitAsync(InitializeTimeout, cancellationToken).ConfigureAwait(false);
        var audioRecorder = Services.GetRequiredService<AudioRecorder>();
        await audioRecorder.WhenInitialized.ConfigureAwait(false);
    }

    // ReSharper disable once InconsistentNaming
    private bool CanUseNNVad()
    {
        if (HostInfo.ClientKind != ClientKind.Ios)
            return true;

        var deviceModel = HostInfo.DeviceModel;
        var match = IosDeviceRegex.Match(deviceModel);
        if (!match.Success)
            return false;

        if (OrdinalIgnoreCaseEquals( match.Groups["type"].Value,"mac"))
            return true;

        // only recent versions of apple hw have decent performance to run NN with WASM SIMD for VAD
        var hwVersion = match.Groups["version"].Value;
        return NumberExt.TryParsePositiveLong(hwVersion, out var v) && v >= 11;
    }

    private async Task UpdateBackgroundState(CancellationToken cancellationToken)
    {
        var prevState = (ActivityState?)null; // Assuming "unknown"
        var changes = AppActivity.State.Changes(cancellationToken);
        await foreach (var cState in changes.ConfigureAwait(false)) {
            var state = cState.Value;
            if (state == prevState)
                continue;

            Log.LogInformation("Background state has changed: {OldState} -> {State}", prevState, state);
            prevState = state;
            await JS
                .InvokeVoidAsync(JSUpdateBackgroundStateMethod, CancellationToken.None, state.ToString())
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
