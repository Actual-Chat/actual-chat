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

    private static readonly Lock WriteLock = new();
    private static CpuTimestamp _startedAt;
    private static FilePath _filePath;
    private static FilePath _previousFilePath;
    private static volatile bool _isInitialized;

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
            if (File.Exists(_filePath))
                File.Move(_filePath, _previousFilePath, overwrite: true);
            _isInitialized = true;
            Add("process started");
        }
        catch {
            _isInitialized = false;
        }
    }

    public static void Add(string phase)
    {
        if (!_isInitialized)
            return;

        try {
            var elapsed = CpuTimestamp.Now - _startedAt;
            lock (WriteLock)
                File.AppendAllText(_filePath, $"+{elapsed.TotalSeconds:F3}s {phase}\n");
        }
        catch {
            // Intended: see Initialize
        }
    }

    public static string ReadPrevious()
    {
        if (!_isInitialized || !File.Exists(_previousFilePath))
            return "";

        try {
            var text = File.ReadAllText(_previousFilePath);
            return text.Length <= MaxPreviousLength ? text : text[..MaxPreviousLength];
        }
        catch {
            return "";
        }
    }
}
