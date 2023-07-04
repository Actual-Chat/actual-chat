using ActualChat.UI.Blazor.Services;
using Stl.IO;

namespace ActualChat.App.Maui.Services;

#pragma warning disable MA0056
#pragma warning disable MA0064

// ReSharper disable once InconsistentNaming
public sealed class SQLiteClientComputedCache : AppClientComputedCache
{
    public new record Options : AppClientComputedCache.Options
    {
        public FilePath DbPath { get; init; }

        public Options()
            => ReadBatchConcurrencyLevel = HardwareInfo.ProcessorCount.Clamp(1, 16);
    }

    private new Options Settings { get; }

    public SQLiteClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = new SQLiteBatchingKvasBackend(settings.DbPath, settings.Version, services);
    }
}
