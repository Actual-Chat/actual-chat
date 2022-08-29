using Stl.IO;
using Stl.Locking;

namespace ActualChat.Kubernetes;

public interface IKubeInfo
{
    ValueTask<Kube?> GetKube(CancellationToken cancellationToken = default);
    ValueTask<bool> HasKube(CancellationToken cancellationToken = default);
    ValueTask<Kube> RequireKube(CancellationToken cancellationToken = default);
}

public class KubeInfo : IKubeInfo, IAsyncDisposable
{
    public FilePath TokenPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    public FilePath CACertPath { get; init; } = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

    private IServiceProvider Services { get; }

    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);
    private volatile CachedKube? _cachedInfo;
    private volatile KubeToken? _token;

    public KubeInfo(IServiceProvider services)
        => Services = services;

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
        if (podIP.IsNullOrEmpty() || host.IsNullOrEmpty() || port == 0) {
            _cachedInfo = new CachedKube(null);
            return null;
        }

        _token = new KubeToken(Services, TokenPath);
        var info = new Kube(host, port, podIP, _token);
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
