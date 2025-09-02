using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public interface IUploadSessionRepository
{
    Task SaveAsync(UploadSession session);
    Task<UploadSession?> GetAsync(string sessionId);
    Task<IEnumerable<UploadSession>> GetAllAsync();
    Task DeleteAsync(string sessionId);
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

    public async Task SaveAsync(UploadSession session)
    {
        await _internal.Set(Key(session.SessionId), session).ConfigureAwait(false);
        await _internal.Flush().ConfigureAwait(false);
    }

    public Task<UploadSession?> GetAsync(string sessionId)
        => _internal.Get<UploadSession>(Key(sessionId)).AsTask();

    public async Task<IEnumerable<UploadSession>> GetAllAsync()
        => (await _internal.GetAll<UploadSession>().ConfigureAwait(false))
            .Select(c => c.Item2)
            .ToArray();

    public Task DeleteAsync(string sessionId)
        => _internal.Set(Key(sessionId), null);
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
