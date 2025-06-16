using ActualChat.UI.Blazor.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

#pragma warning disable MA0064

// ReSharper disable once InconsistentNaming
public sealed class SQLiteRemoteComputedCache : AppRemoteComputedCache
{
    public new record Options : AppRemoteComputedCache.Options
    {
        public FilePath DbPath { get; init; }

        public Options()
            => ReaderWorkerPolicy = new BatchProcessorWorkerPolicy() {
                MinWorkerCount = 2,
                MaxWorkerCount = HardwareInfo.ProcessorCount.Clamp(2, 16),
            };
    }

    private new Options Settings { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SQLiteRemoteComputedCache))]
    public SQLiteRemoteComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = new SQLiteBatchingKvasBackend(settings.DbPath, settings.Version, services);
        _ = Reader.Start();
    }
}
