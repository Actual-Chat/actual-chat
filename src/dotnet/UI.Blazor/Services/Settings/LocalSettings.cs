using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class LocalSettings : BatchingKvas
{
    public new record Options : BatchingKvas.Options;

    public LocalSettings(Options options, LocalSettingsBackend backend, ILogger<LocalSettings>? log = null)
        : base(options, backend, log) { }
}
