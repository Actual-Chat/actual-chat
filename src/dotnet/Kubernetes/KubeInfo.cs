using Stl.IO;
using Stl.Locking;

namespace ActualChat.Kubernetes;

public interface IKubeInfo
{
    ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default);
    ValueTask<bool> HasKube(CancellationToken cancellationToken = default);
    ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default);
}

public sealed class KubeInfo(IServiceProvider services) : IKubeInfo, IAsyncDisposable
{
    public FilePath TokenPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public FilePath CACertPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

    private IServiceProvider Services { get; } = services;

    private readonly AsyncLock _asyncLock = AsyncLock.New(LockReentryMode.CheckedPass);
    private volatile CachedKube? _cachedInfo;
    private volatile KubeToken? _token;

    public async ValueTask DisposeAsync()
    {
        if (_token == null) return;
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        if (_token == null) return;

        await _token.DisposeAsync().ConfigureAwait(false);
        _token = null;
    }

    public async ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default)
    {
        // Double check locking
        if (_cachedInfo != null) return _cachedInfo.Value;
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        if (_cachedInfo != null) return _cachedInfo.Value;

        var host = KubeEnvironmentVars.KubernetesServiceHost;
        var port = KubeEnvironmentVars.KubernetesServicePort;
        var podIP = KubeEnvironmentVars.PodIP;
        var isKubeAvailable = !podIP.IsNullOrEmpty() && !host.IsNullOrEmpty() && port != 0;

        if (isKubeAvailable) {
            _token = new KubeToken(Services, TokenPath);
        }
        else if (Constants.DebugMode.KubeEmulation) {
            host = "this.host.does.not.exist";
            port = 53; // DNS port, definitely can't run Kubernetes service
            var urlMapper = Services.GetRequiredService<UrlMapper>();
            podIP = urlMapper.BaseUri.Host;
            _token = new KubeToken(Services, "");
        }

        var info = _token != null ? new Kube(host, port, podIP, _token) : null;
        _cachedInfo = new CachedKube(info);
        return info;
    }

    public async ValueTask<bool> HasKube(CancellationToken cancellationToken = default)
    {
        var info = await GetKube(cancellationToken).ConfigureAwait(false);
        return info != null;
    }

    public async ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default)
    {
        var info = await GetKube(cancellationToken).ConfigureAwait(false);
        return info ?? throw StandardError.NotSupported("This method should be executed within Kubernetes cluster.");
    }

    // Nested types

    private record CachedKube(Kube? Value);
}
