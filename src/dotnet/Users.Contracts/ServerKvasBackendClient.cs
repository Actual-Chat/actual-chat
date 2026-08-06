using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Client-side KVAS implementation that delegates to <see cref="IServerKvasBackend"/>.
/// </summary>
public class ServerKvasBackendClient(
    IServerKvasBackend serverKvasBackend,
    string prefix,
    bool isOutermost = false
    ) : IKvas
{
    public IServiceProvider Services => field ??= ServerKvasBackend.GetServices();

    private string Prefix { get; } = prefix;
    // Writes from a service running its own DbOperationScope must be outermost: this store lives
    // in the Users DB, and one operation can't span two DbContexts.
    private bool IsOutermost { get; } = isOutermost;
    private IServerKvasBackend ServerKvasBackend { get; } = serverKvasBackend;
    private ICommander Commander { get; } = serverKvasBackend.GetCommander();

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => ServerKvasBackend.Get(Prefix, key, cancellationToken).ToValueTask();

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        var command = new ServerKvasBackend_SetMany(Prefix, (key, value));
        return Commander.Call(command, IsOutermost, cancellationToken);
    }

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        var command = new ServerKvasBackend_SetMany(Prefix, items);
        return Commander.Call(command, IsOutermost, cancellationToken);
    }
}
