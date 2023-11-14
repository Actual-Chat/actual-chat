using System.Text.RegularExpressions;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed partial class AudioInitializer(IServiceProvider services) : WorkerBase, IAudioInfoBackend
{
    private static readonly string JSInitMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.init";
    private static readonly string JSUpdateBackgroundStateMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.setBackgroundState";
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(5);

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IosDeviceRegexFactory();
    private static readonly Regex IosDeviceRegex = IosDeviceRegexFactory();

    private readonly TaskCompletionSource _whenInitializedSource = TaskCompletionSourceExt.New();
    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;
    private BackgroundUI? _backgroundUI;
    private HostInfo? _hostInfo;
    private IJSRuntime? _js;
    private UrlMapper? _urlMapper;
    private ILogger? _log;

    private IServiceProvider Services { get; } = services;
    private IJSRuntime JS => _js ??= Services.JSRuntime();
    private UrlMapper UrlMapper => _urlMapper ??= Services.UrlMapper();
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private BackgroundUI BackgroundUI => _backgroundUI ??= Services.GetRequiredService<BackgroundUI>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public Task WhenInitialized => _whenInitializedSource.Task;

    protected override Task DisposeAsyncCore()
    {
        _backendRef?.DisposeSilently();
        return base.DisposeAsyncCore();
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogInformation("AudioInitializer: started");
        var retryDelays = RetryDelaySeq.Exp(0.5, 3);
        var whenInitialized = AsyncChainExt.From(Initialize, $"{nameof(AudioInitializer)}.{nameof(Initialize)}")
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .Run(cancellationToken);
        await _whenInitializedSource.TrySetFromTaskAsync(whenInitialized, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("AudioInitializer: initialized with status {Status}", whenInitialized.Status);

        await AsyncChainExt.From(UpdateBackgroundState, $"{nameof(AudioInitializer)}.{nameof(UpdateBackgroundState)}")
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .Run(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task Initialize(CancellationToken cancellationToken)
    {
        var cts = cancellationToken.CreateLinkedTokenSource();
        try {
            Log.LogInformation("AudioInitializer: Initialize() is being called...");
            var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
            cts.CancelAfter(InitializeTimeout);
            await JS
                .InvokeVoidAsync(JSInitMethod, cts.Token, backendRef, UrlMapper.BaseUrl, CanUseNNVad())
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException) {
            if (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw StandardError.Timeout($"{nameof(AudioInitializer)}.{nameof(Initialize)}");

            throw;
        }

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
        return int.TryParse(match.Groups["version"].Value, CultureInfo.InvariantCulture, out var hwVersion)
            && hwVersion >= 11;
    }

    private async Task UpdateBackgroundState(CancellationToken cancellationToken)
    {
        var prevState = (BackgroundState?)null; // Assuming "unknown"
        var changes = BackgroundUI.State.Changes(cancellationToken);
        await foreach (var cState in changes.ConfigureAwait(false)) {
            var state = cState.Value;
            if (state == prevState)
                continue;

            Log.LogInformation("Background state has changed: {OldState} -> {State}", prevState, state);
            prevState = state;
            await JS
                .InvokeVoidAsync(JSUpdateBackgroundStateMethod, cancellationToken, state.ToString())
                .ConfigureAwait(false);
        }
    }
}
