using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Services;
using Stl.IO;

namespace ActualChat.App.Maui.Services;

#pragma warning disable MA0064

// ReSharper disable once InconsistentNaming
public sealed class SQLiteClientComputedCache : AppClientComputedCache
{
    public new record Options : AppClientComputedCache.Options
    {
        public FilePath DbPath { get; init; }

        public Options()
            => ReaderWorkerPolicy = new BatchProcessorWorkerPolicy() {
                MinWorkerCount = 2,
                MaxWorkerCount = HardwareInfo.ProcessorCount.Clamp(2, 16),
            };
    }

    private new Options Settings { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SQLiteClientComputedCache))]
    public SQLiteClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = new SQLiteBatchingKvasBackend(settings.DbPath, settings.Version, services);
        _ = Reader.Start();
    }
}
