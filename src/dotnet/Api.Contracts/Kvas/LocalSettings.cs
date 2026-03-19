namespace ActualChat.Kvas;

/// <summary>
/// Local key-value store for client-side settings persistence.
/// </summary>
public class LocalSettings : BatchingKvas
{
    public new record Options : BatchingKvas.Options
    {
        public required Func<IServiceProvider, IBatchingKvasBackend> BackendFactory { get; init; }
    }

    public new Options Settings { get; }

    public LocalSettings(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = settings.BackendFactory.Invoke(services);
        _ = Reader.Start();
    }
}
