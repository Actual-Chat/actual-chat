using System.Text.RegularExpressions;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed partial class AudioInitializer : WorkerBase, IAudioInfoBackend
{
    private static readonly string JSInitMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.init";
    private static readonly string JSUpdateBackgroundStateMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.updateBackgroundState";

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IosDeviceRegexFactory();
    private static readonly Regex IosDeviceRegex = IosDeviceRegexFactory();

    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;
    private BackgroundUI? _backgroundUI;
    private HostInfo? _hostInfo;
    private IJSRuntime? _js;
    private UrlMapper? _urlMapper;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private IJSRuntime JS => _js ??= Services.JSRuntime();
    private UrlMapper UrlMapper => _urlMapper ??= Services.UrlMapper();
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private BackgroundUI BackgroundUI => _backgroundUI ??= Services.GetRequiredService<BackgroundUI>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public Task WhenInitialized { get; }

    public AudioInitializer(IServiceProvider services)
    {
        Services = services;
        WhenInitialized = Initialize();
    }

    protected override Task DisposeAsyncCore()
    {
        _backendRef?.DisposeSilently();
        return base.DisposeAsyncCore();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogInformation("AudioInitializer: started");
        var baseChains = new[] {
            AsyncChainExt.From(UpdateBackgroundState),
        };
        var retryDelays = RetryDelaySeq.Exp(0.5, 3);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private Task Initialize()
        => new AsyncChain(nameof(Initialize), async ct => {
                // ReSharper disable once InconsistentNaming
                var deviceModel = HostInfo.DeviceModel;

                // ReSharper disable once InconsistentNaming
                var canUseNNVad = HostInfo.ClientKind != ClientKind.Ios || IsIOSDeviceFastEnoughToRunNNVad(deviceModel);
                var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
                await JS.InvokeVoidAsync(JSInitMethod, ct, backendRef, UrlMapper.BaseUrl, canUseNNVad).ConfigureAwait(false);
                this.Start();
            })
            .Log(LogLevel.Debug, Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
            .RunIsolated();

    // ReSharper disable once InconsistentNaming
    private static bool IsIOSDeviceFastEnoughToRunNNVad(string deviceModel)
    {
        var match = IosDeviceRegex.Match(deviceModel);
        if (!match.Success)
            return false;

        if (OrdinalIgnoreCaseEquals( match.Groups["type"].Value,"mac"))
            return true;

        // only recent versions of apple hw have decent performance to run NN with WASM SIMD for VAD
        return int.TryParse(match.Groups["version"].Value, CultureInfo.InvariantCulture, out var hwVersion)
            && hwVersion >= 12;
    }

    private async Task UpdateBackgroundState(CancellationToken cancellationToken)
    {
        var previousState = BackgroundState.Foreground;
        var stateChanges = BackgroundUI.State.Changes(cancellationToken);
        await foreach (var cState in stateChanges.ConfigureAwait(false)) {
            var state = cState.Value;
            if (state.IsActive() != previousState.IsActive()) {
                Log.LogInformation("Activity state has changed: {OldState} -> {State}", previousState, state);
                await JS.InvokeVoidAsync(JSUpdateBackgroundStateMethod, cancellationToken, state.ToString())
                    .ConfigureAwait(false);
            }
            else
                Log.LogInformation("Activity state change ignored: {OldState} -> {State}", previousState, state);

            previousState = state;
        }
    }
}
