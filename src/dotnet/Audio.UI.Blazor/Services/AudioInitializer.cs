using System.Text.RegularExpressions;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed partial class AudioInitializer : IAudioInfoBackend, IDisposable
{
    private static readonly string JSInitMethod = $"{AudioBlazorUIModule.ImportName}.AudioInitializer.init";

    [GeneratedRegex(@"^(?<type>mac|iPhone|iPad)(?:(?<version>\d+),\d*)?$")]
    private static partial Regex IOSDeviceRegexFactory();
    private static readonly Regex IOSDeviceRegex = IOSDeviceRegexFactory();

    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private IJSRuntime JS { get; }
    private UrlMapper UrlMapper { get; }
    private HostInfo HostInfo { get; }

    public Task WhenInitialized { get; }

    public AudioInitializer(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        JS = services.JSRuntime();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        HostInfo = services.GetRequiredService<HostInfo>();
        WhenInitialized = Initialize();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    private Task Initialize()
        => new AsyncChain(nameof(Initialize), async ct => {
                // ReSharper disable once InconsistentNaming
                var deviceModel = HostInfo.DeviceModel;

                // ReSharper disable once InconsistentNaming
                var canUseNNVad = HostInfo.ClientKind != ClientKind.Ios || IsIOSDeviceFastEnoughToRunNNVad(deviceModel);
                var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
                await JS.InvokeVoidAsync(JSInitMethod, ct, backendRef, UrlMapper.BaseUrl, canUseNNVad).ConfigureAwait(false);
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
}
