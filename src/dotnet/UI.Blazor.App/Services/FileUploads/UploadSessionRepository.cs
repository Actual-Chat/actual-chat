using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public interface IUploadSessionRepository
{
    Task Save(UploadSession session, bool flush = true);
    Task<UploadSession?> Get(string sessionId);
    Task<IEnumerable<KeyValuePair<string, UploadSession>>> GetAll();
    Task Delete(string sessionId);
    Task Flush();
}

public class UploadSessionRepository : IUploadSessionRepository
{
    private readonly UploadSessionRepositoryInternal _internal;

    private const string Prefix = "";
    private static string Key(string sessionId) => $"{Prefix}{sessionId}";

    public UploadSessionRepository(IServiceProvider services)
    {
        var options = services.GetRequiredService<UploadSessionRepositoryInternal.Options>();
        _internal = new UploadSessionRepositoryInternal(options, services);
    }

    public async Task Save(UploadSession session, bool flush = true)
    {
        await _internal.Set(Key(session.SessionId), session).ConfigureAwait(false);
        if (flush)
            await _internal.Flush().ConfigureAwait(false);
    }

    public Task<UploadSession?> Get(string sessionId)
        => _internal.Get<UploadSession>(Key(sessionId)).AsTask();

    public async Task<IEnumerable<KeyValuePair<string, UploadSession>>> GetAll()
        => (await _internal.GetAll<UploadSession>().ConfigureAwait(false))
            .Select(c => new KeyValuePair<string, UploadSession>(c.Item1, c.Item2))
            .ToArray();

    public Task Delete(string sessionId)
        => _internal.Set(Key(sessionId), null);

    public Task Flush()
        => _internal.Flush();
}

internal class UploadSessionRepositoryInternal : BatchingKvas
{
    public new record Options : BatchingKvas.Options
    {
        public required Func<IServiceProvider, IBatchingKvasBackend> BackendFactory { get; init; }
    }

    public new Options Settings { get; }

    public UploadSessionRepositoryInternal(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = settings.BackendFactory.Invoke(services);
        _ = Reader.Start();
    }
}
