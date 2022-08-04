using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class LocalStorage : BatchingKvas
{
    public new record Options : BatchingKvas.Options;

    public LocalStorage(Options options, LocalStorageBackend backend, ILogger<LocalStorage>? log = null)
        : base(options, backend, log) { }
}
