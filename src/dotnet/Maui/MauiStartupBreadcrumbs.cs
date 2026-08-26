using ActualLab.IO;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

/// <summary>
/// Persists coarse startup-phase marks to a cache file, so when an ANR or a crash kills
/// the process, the next launch can report which phase the previous process died in.
/// </summary>
public static class MauiStartupBreadcrumbs
{
    private const int MaxPreviousLength = 2048;
    private static readonly TimeSpan FlushDelay = TimeSpan.FromMilliseconds(250);

    private static readonly Lock WriteLock = new();
    private static readonly List<string> PendingLines = new();
    private static CpuTimestamp _startedAt;
    private static FilePath _filePath;
    private static FilePath _previousFilePath;
    private static System.Threading.Timer? _flushTimer;
    private static bool _isRotated;
    // Publication guard for the fields Initialize sets; accessed via Volatile.Read/Write.
    private static bool _isInitialized;

    public static void Initialize()
    {
        // Called before logging is configured, so any failure here must be swallowed silently;
        // losing breadcrumbs is always better than failing the startup.
        if (!MauiSettings.Diagnostics.EnableStartupBreadcrumbs)
            return;

        try {
            _startedAt = CpuTimestamp.Now;
            var cacheDir = (FilePath)FileSystem.CacheDirectory;
            _filePath = cacheDir & "startup-breadcrumbs.txt";
            _previousFilePath = cacheDir & "startup-breadcrumbs.prev.txt";
            Volatile.Write(ref _isInitialized, true);
            Add("Process started");
        }
        catch {
            Volatile.Write(ref _isInitialized, false);
        }
    }

    public static void Add(string phase)
    {
        // Buffered: marks land on the startup path's main thread, where a synchronous append per
        // mark would feed the very stalls the breadcrumbs exist to diagnose. A timer flushes each
        // burst in one append; an ANR kill fires no earlier than 10s in, so nothing that matters
        // is still pending by then.
        if (!Volatile.Read(ref _isInitialized))
            return;

        try {
            var elapsed = CpuTimestamp.Now - _startedAt;
            lock (WriteLock) {
                PendingLines.Add($"+{elapsed.TotalSeconds:F3}s {phase}\n");
                _flushTimer ??= new System.Threading.Timer(
                    _ => Flush(), null, FlushDelay, System.Threading.Timeout.InfiniteTimeSpan);
            }
        }
        catch {
            // Intended: see Initialize
        }
    }

    public static string ReadPrevious()
    {
        if (!Volatile.Read(ref _isInitialized))
            return "";

        try {
            lock (WriteLock)
                EnsureRotated();
            if (!File.Exists(_previousFilePath))
                return "";

            var text = File.ReadAllText(_previousFilePath);
            return text.Length <= MaxPreviousLength ? text : text[..MaxPreviousLength];
        }
        catch {
            return "";
        }
    }

    // Private methods

    private static void Flush()
    {
        try {
            lock (WriteLock) {
                _flushTimer?.Dispose();
                _flushTimer = null;
                if (PendingLines.Count == 0)
                    return;

                EnsureRotated();
                File.AppendAllText(_filePath, string.Concat(PendingLines));
                PendingLines.Clear();
            }
        }
        catch {
            // Intended: see Initialize
        }
    }

    private static void EnsureRotated()
    {
        // Deferred from Initialize to keep its file IO off the main thread too.
        if (_isRotated)
            return;

        if (File.Exists(_filePath))
            File.Move(_filePath, _previousFilePath, overwrite: true);
        _isRotated = true;
    }
}
