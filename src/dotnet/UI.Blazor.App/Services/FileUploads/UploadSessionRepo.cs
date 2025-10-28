using ActualChat.Kvas;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class UploadSessionRepo
{
    private readonly UploadSessionRepositoryInternal _internal;

    private const string Prefix = "";
    private static string Key(string sessionId) => $"{Prefix}{sessionId}";

    public UploadSessionRepo(IServiceProvider services)
    {
        var options = new UploadSessionRepositoryInternal.Options {
            BackendFactory = c => new WebKvasBackend($"{BlazorUIAppModule.ImportName}.uploadSessions", c),
        };
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
        => (await _internal.ListAllEntries<UploadSession>().ConfigureAwait(false))
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
