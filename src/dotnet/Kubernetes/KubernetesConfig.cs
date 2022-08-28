using Microsoft.Toolkit.HighPerformance;
using Stl.Locking;

namespace ActualChat.Kubernetes;

public sealed class KubernetesConfig : IDisposable
{
    private const string TokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
    private const string CACertPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

    private static readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private static volatile object? _isInCluster; // Has to be atomic
    private static volatile KubernetesConfig? _config;

    public string Host { get; }
    public int Port { get; }
    public AuthToken Token { get; }

    private KubernetesConfig(string host, int port, AuthToken token)
    {
        Host = host;
        Port = port;
        Token = token;
    }

    public static async Task<KubernetesConfig> Get(IStateFactory stateFactory, CancellationToken cancellationToken)
    {
        if (!await IsInCluster(cancellationToken).ConfigureAwait(false))
            throw StandardError.NotSupported<KubernetesConfig>(
                $"{nameof(Get)} should be executed withing Kubernetes cluster");

        // Double check locking
        if (_config != null) return _config;
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        if (_config != null) return _config;

        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")!;
        var port = int.Parse(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT")!, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var token = await AuthToken.CreateToken(stateFactory, cancellationToken).ConfigureAwait(false);
        var config = new KubernetesConfig(host, port, token);
        _config = config;

        return config;
    }

    public static async ValueTask<bool> IsInCluster(CancellationToken cancellationToken)
    {
        if (_isInCluster is bool isInCluster)
            return isInCluster;

        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(port)) {
            _isInCluster = false;
            return false;
        }

        var tokenExistsTask = Task.Run(() => File.Exists(TokenPath), cancellationToken);
        var caExistsTask = Task.Run(() => File.Exists(CACertPath), cancellationToken);
        var tokenExists = await tokenExistsTask.ConfigureAwait(false);
        var caExists = await caExistsTask.ConfigureAwait(false);
        isInCluster = tokenExists && caExists;
        _isInCluster = isInCluster;
        return isInCluster;
    }

    public void Dispose()
        => Token.Dispose();

    public class AuthToken : WorkerBase
    {
        public IMutableState<string> State { get; }

        private AuthToken(IStateFactory stateFactory, string value)
            => State = stateFactory.NewMutable(value);

        public static async Task<AuthToken> CreateToken(IStateFactory stateFactory, CancellationToken cancellationToken)
        {
            if (!await IsInCluster(cancellationToken).ConfigureAwait(false))
                throw StandardError.NotSupported<AuthToken>(
                    $"{nameof(CreateToken)} should be executed withing Kubernetes cluster");

            var tokenValue = (await File.ReadAllTextAsync(TokenPath, cancellationToken).ConfigureAwait(false)).Trim();

            var token = new AuthToken(stateFactory, tokenValue);
            token.Start();

            return token;
        }

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            var watcher = new FileSystemWatcher();
            watcher.Path = Path.GetDirectoryName(TokenPath)!;
            watcher.Filter = Path.GetFileName(TokenPath);
            watcher.NotifyFilter = NotifyFilters.LastWrite;
            watcher.Changed += OnChanged;
            watcher.EnableRaisingEvents = true;

            await WaitForCancellation().ConfigureAwait(false);

            async Task WaitForCancellation() {
                using var dTask = cancellationToken.ToTask();
                await dTask.Resource.ConfigureAwait(false);
            }

            void OnChanged(object sender, FileSystemEventArgs e) {
                _ = BackgroundTask.Run(async () => {
                    var tokenValue = await File.ReadAllTextAsync(TokenPath, cancellationToken).ConfigureAwait(false);
                    tokenValue = tokenValue.Trim();
                    State.Value = tokenValue;
                }, cancellationToken);
            }
        }
    }
}
