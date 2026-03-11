using ActualChat.Hashing;
using ActualChat.Hosting;
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

    private FilePath GetCacheDir()
    {
        if (!Settings.AvatarPicturesCacheDir.IsNullOrEmpty())
            return Settings.AvatarPicturesCacheDir;

        var baseDir = FilePath.GetApplicationTempDirectory();
        if (HostInfo.IsTested)
            baseDir |= $"tst-{CoreSettings.Instance}";
        return baseDir | "avatars";
    }

    public async Task<FilePath> Get(AvatarQuery query, CancellationToken cancellationToken)
    {
        var filePath = GetCacheFilePath(query);

        // Return cached file if exists
        if (File.Exists(filePath))
            return filePath;

        await GenerateToFile(query, filePath, cancellationToken).ConfigureAwait(false);
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

    private FilePath GetCacheFilePath(AvatarQuery query)
    {
        var ext = query.Format == AvatarFormat.Png ? ".png" : ".svg";
        var cacheKey = $"{query.Kind}_{query.Key}_{query.Format}_{query.Size ?? 0}_{query.Title ?? ""}";
        var hash = cacheKey.Hash().SHA256().AlphaNumeric();
        return (CacheDir | hash).ChangeExtension(ext);
    }

    private void EnsureCacheDir()
    {
        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);
    }
}
