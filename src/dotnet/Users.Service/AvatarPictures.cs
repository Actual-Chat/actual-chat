using ActualChat.Hashing;
using ActualChat.Module;
using ActualChat.Users.AvatarIcons;
using ActualChat.Users.Module;
using ActualLab.IO;

namespace ActualChat.Users;

/// <summary>
/// Service for avatar picture generation with file caching.
/// </summary>
public sealed class AvatarPictures(IServiceProvider services)
{
    private FilePath? _cacheDir;

    private ILogger Log => field ??= services.LogFor<AvatarPictures>();
    private UsersSettings Settings => field ??= services.GetRequiredService<UsersSettings>();
    private HostInfo HostInfo => field ??= services.GetRequiredService<HostInfo>();
    private CoreSettings CoreSettings => field ??= services.GetRequiredService<CoreSettings>();
    private FilePath CacheDir => _cacheDir ??= GetCacheDir();
    private ThreadSafeLruCache<string, FilePath> Cache => field ??= CreateCache();

    private FilePath GetCacheDir()
    {
        if (!Settings.AvatarPicturesCacheDir.IsNullOrEmpty())
            return Settings.AvatarPicturesCacheDir;

        var baseDir = FilePath.GetApplicationTempDirectory();
        if (HostInfo.IsTested)
            baseDir |= $"tst-{CoreSettings.Instance}";
        return baseDir | "avatars";
    }

    private ThreadSafeLruCache<string, FilePath> CreateCache()
    {
        var cache = new ThreadSafeLruCache<string, FilePath>(
            Settings.AvatarPicturesCacheCapacity,
            null,
            static (_, path) => path.DeleteSilently());

        if (!Directory.Exists(CacheDir))
            return cache;

        // Load existing files into cache (oldest first so recent ones are "most recent")
        var files = Directory.EnumerateFiles(CacheDir)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastWriteTimeUtc);
        foreach (var file in files)
            cache.TryAdd(Path.GetFileNameWithoutExtension(file.Name), file.FullName);
        return cache;
    }

    public async Task<FilePath> Get(AvatarQuery query, CancellationToken cancellationToken)
    {
        var key = GetCacheKey(query);

        // Check LRU cache and file existence
        if (Cache.TryGetValue(key, out var cached) && File.Exists(cached))
            return cached;

        var filePath = GetFilePath(query, key);
        await GenerateToFile(query, filePath, cancellationToken).ConfigureAwait(false);
        Cache[key] = filePath;
        return filePath;
    }

    private async Task GenerateToFile(AvatarQuery query, FilePath filePath, CancellationToken cancellationToken)
    {
        try {
            EnsureCacheDir();
            if (query.Format == AvatarFormat.Png) {
                var size = query.Size ?? 80;
                if (query.Kind == AvatarKind.Marble)
                    MarbleAvatars.GeneratePng(query.Key, filePath, title: query.Title ?? "", size: size);
                else
                    BeamAvatars.GeneratePng(query.Key, filePath, size: size);
            }
            else {
                var svg = query.Kind == AvatarKind.Marble
                    ? MarbleAvatars.GenerateSvg(query.Key, title: query.Title ?? "")
                    : BeamAvatars.GenerateSvg(query.Key);
                await File.WriteAllTextAsync(filePath, svg, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to generate avatar to '{Path}'", filePath);
            throw;
        }
    }

    private static string GetCacheKey(AvatarQuery query)
    {
        var cacheKey = $"{query.Kind}_{query.Key}_{query.Format}_{query.Size ?? 0}_{query.Title ?? ""}";
        return cacheKey.Hash().SHA256().AlphaNumeric();
    }

    private FilePath GetFilePath(AvatarQuery query, string key)
    {
        var ext = query.Format == AvatarFormat.Png ? ".png" : ".svg";
        return (CacheDir | key).ChangeExtension(ext);
    }

    private void EnsureCacheDir()
    {
        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);
    }
}
