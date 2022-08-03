using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class LocalStorage : KvasForBackend
{
    public new record Options : KvasForBackend.Options;

    public LocalStorage(Options options, LocalStorageBackend backend, ILogger<LocalStorage>? log = null)
        : base(options, backend, log) { }
}
