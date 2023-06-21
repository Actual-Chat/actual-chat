using ActualChat.Kvas;

namespace ActualChat.Users;

public class ServerKvasBackendClient : IKvas
{
    private string Prefix { get; }
    private IServerKvasBackend ServerKvasBackend { get; }
    private ICommander Commander { get; }

    public ServerKvasBackendClient(IServerKvasBackend serverKvasBackend, string prefix)
    {
        ServerKvasBackend = serverKvasBackend;
        Prefix = prefix;
        Commander = serverKvasBackend.GetCommander();
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => ServerKvasBackend.Get(Prefix, key, cancellationToken).ToValueTask();

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
    {
        var command = new ServerKvasBackend_SetMany(Prefix, (key, value));
        return Commander.Call(command, cancellationToken);
    }

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
    {
        var command = new ServerKvasBackend_SetMany(Prefix, items);
        return Commander.Call(command, cancellationToken);
    }
}
