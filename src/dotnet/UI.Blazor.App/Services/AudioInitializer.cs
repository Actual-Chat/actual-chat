using System.Text.RegularExpressions;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed partial class AudioInitializer(UIHub hub)
    : UIWorkerBase<UIHub>(hub), IAudioInitializer, IAudioInfoBackend
{
    private static readonly string JSInitMethod = $"{BlazorUIAppModule.ImportName}.AudioInitializer.init";
    private static readonly string JSSetBackgroundActivityStateMethod = $"{BlazorUIAppModule.ImportName}.AudioInitializer.setBackgroundActivityState";
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(5);

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IosDeviceRegexFactory();
    private static readonly Regex IosDeviceRegex = IosDeviceRegexFactory();

    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;

    private IAppActivityState ActivityState => field ??= Services.GetRequiredService<IAppActivityState>();

    public Task WhenInitialized => _whenInitializedSource.Task;

    public void StartInitialization()
        => _ = this.Start();

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("AudioInitializer: started");
            var retryDelays = RetryDelaySeq.Exp(0.1, 3);
            var whenInitialized = AsyncChain.From(Initialize, $"{nameof(AudioInitializer)}.{nameof(Initialize)}")
                .Log(LogLevel.Debug, Log)
                .Retry(retryDelays, 5, Log)
                .Run(cancellationToken);
            await _whenInitializedSource.TrySetFromTaskAsync(whenInitialized, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("AudioInitializer: initialized with status {Status}", whenInitialized.Status);

            await AsyncChain.From(PushBackgroundActivityState, $"{nameof(AudioInitializer)}.{nameof(PushBackgroundActivityState)}")
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
    }

    // ReSharper disable once InconsistentNaming
    private bool CanUseNNVad()
    {
        if (HostInfo.AppKind != AppKind.Ios)
            return true;

        var deviceModel = HostInfo.DeviceModel;
        var match = IosDeviceRegex.Match(deviceModel);
        if (!match.Success)
            return false;

        if (string.Equals( match.Groups["type"].Value,"mac", StringComparison.OrdinalIgnoreCase))
            return true;

        // only recent versions of apple hw have decent performance to run NN with WASM SIMD for VAD
        var hwVersion = match.Groups["version"].Value;
        return NumberExt.TryParsePositiveLong(hwVersion, out var v) && v >= 11;
    }

    private async Task PushBackgroundActivityState(CancellationToken cancellationToken)
    {
        var prevState = (AppActivityState?)null; // Assuming "unknown"
        var changes = ActivityState.State.Computed.ChangesUntyped(cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var cState = (Computed<AppActivityState>)c;
            var state = cState.Value;
            if (state == prevState)
                continue;

            Log.LogInformation("AppActivityState.State changed: {OldState} -> {State}", prevState, state);
            prevState = state;
            await JS
                .InvokeVoidAsync(JSSetBackgroundActivityStateMethod, CancellationToken.None, state.ToString())
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
