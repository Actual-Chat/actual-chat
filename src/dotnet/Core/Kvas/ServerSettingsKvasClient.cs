namespace ActualChat.Kvas;

public class ServerSettingsKvasClient(IServerSettings serverSettings, Session session) : IKvas
{
    public IServerSettings ServerSettings { get; } = serverSettings;
    public Session Session { get; } = session;

    public async ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
    {
        var bytes = await ServerSettings.Get(Session, key, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    public async Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        var command = new ServerSettings_Set(Session, key, value);
        await ServerSettings.GetCommander().Call(command, true, cancellationToken).ConfigureAwait(false);
    }

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => Task.FromException(StandardError.NotSupported("Server settings does not support batching."));
}
