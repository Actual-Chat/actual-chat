using System.Text.RegularExpressions;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed partial class AudioInitializer : WorkerBase, IAudioInfoBackend, INotifyInitialized
{
    private static readonly string JSInitMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.init";
    private static readonly string JSUpdateBackgroundStateMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.updateBackgroundState";

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IOSDeviceRegexFactory();
    private static readonly Regex IOSDeviceRegex = IOSDeviceRegexFactory();

    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private IJSRuntime JS { get; }
    private UrlMapper UrlMapper { get; }
    private HostInfo HostInfo { get; }
    private BackgroundUI BackgroundUI { get; }

    public Task WhenInitialized { get; }

    public AudioInitializer(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        JS = services.JSRuntime();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        HostInfo = services.GetRequiredService<HostInfo>();
        BackgroundUI = services.GetRequiredService<BackgroundUI>();
        WhenInitialized = Initialize();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    protected override async Task DisposeAsyncCore()
    {
        _backendRef?.DisposeSilently();
        await base.DisposeAsyncCore();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        Log.LogWarning("AudioInitializer - ON RUN");
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
        var match = IOSDeviceRegex.Match(deviceModel);
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
                Log.LogInformation("Activity state has changed. {OldState} -> {State}", previousState, state);
                await JS.InvokeVoidAsync(JSUpdateBackgroundStateMethod, cancellationToken, state.ToString())
                    .ConfigureAwait(false);
            }
            else
                Log.LogInformation("Activity state change skipped. {OldState} -> {State}", previousState, state);

            previousState = state;
        }
    }
}
