using ActualChat.Kvas;
using ActualLab.IO;

namespace ActualChat.Maui.Services;

/// <summary>
/// MAUI-side plumbing for <see cref="KvasarKvas"/>: app-suspension handling and the removal
/// of the SQLite files Kvasar replaced.
/// </summary>
public static class KvasarStoreSupport
{
    private static readonly TimeSpan SuspendTimeout = TimeSpan.FromSeconds(3);

    public static KvasarKvas WithSuspendHandler(this KvasarKvas kvas, IServiceProvider services)
    {
        var hostInfo = services.HostInfo();
        if (hostInfo.AppKind is not (AppKind.Ios or AppKind.MacOS))
            return kvas;

        MauiBackgroundState.RegisterStateHandler(isBackground => {
            if (!isBackground) {
                kvas.Resume();
                return;
            }
            // Task.Run keeps the close off the main thread's synchronization context, since the
            // handler runs synchronously inside MauiBackgroundState.Set and we block on it here.
            if (!Task.Run(kvas.Suspend).Wait(SuspendTimeout))
                services.LogFor(typeof(KvasarStoreSupport)).LogWarning("Suspend timed out");
        });
        return kvas;
    }

    public static void DeleteLegacyDbFiles(IServiceProvider services, params FilePath[] dbPaths)
    {
        var log = services.LogFor(typeof(KvasarStoreSupport));
        foreach (var dbPath in dbPaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" }) {
                var file = (string)dbPath + suffix;
                if (!File.Exists(file))
                    continue;

                try {
                    File.Delete(file);
                    log.LogInformation("Deleted legacy SQLite file: {File}", file);
                }
                catch (Exception e) {
                    log.LogWarning(e, "Failed to delete legacy SQLite file: {File}", file);
                }
            }
    }
}
