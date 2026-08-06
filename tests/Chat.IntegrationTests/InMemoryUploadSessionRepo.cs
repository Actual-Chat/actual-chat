using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.IntegrationTests;

public class InMemoryUploadSessionRepo : IUploadSessionRepo
{
    private readonly ConcurrentDictionary<string, UploadSessionSnapshot> _sessions = new();

    public Task Save(UploadSessionSnapshot session, bool flush = true)
    {
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task<UploadSessionSnapshot?> Get(string sessionId)
        => Task.FromResult(_sessions.GetValueOrDefault(sessionId));

    public Task<IEnumerable<KeyValuePair<string, UploadSessionSnapshot>>> GetAll()
        => Task.FromResult<IEnumerable<KeyValuePair<string, UploadSessionSnapshot>>>(_sessions.ToArray());

    public Task Delete(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    public Task Flush() => Task.CompletedTask;
}
