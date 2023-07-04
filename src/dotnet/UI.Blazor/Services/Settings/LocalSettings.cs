using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class LocalSettings : BatchingKvas
{
    public new record Options : BatchingKvas.Options
    {
        public IBatchingKvasBackend? BackendOverride { get; init; }
    }

    public new Options Settings { get; }

    public LocalSettings(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = settings.BackendOverride
            ?? new WebKvasBackend($"{BlazorUICoreModule.ImportName}.localSettings", services);
    }
}
