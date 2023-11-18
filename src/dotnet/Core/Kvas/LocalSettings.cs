namespace ActualChat.Kvas;

#pragma warning disable CA1724 // The type name Options conflicts in whole or in part with the namespace ...

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
    }
}
