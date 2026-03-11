using System.Text;
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

        // Generate and cache the avatar
        var (bytes, svg) = GenerateAvatar(query);
        var content = bytes ?? Encoding.UTF8.GetBytes(svg!);
        await CacheAvatar(filePath, content, cancellationToken).ConfigureAwait(false);

        return filePath;
    }

    private (byte[]? Bytes, string? Svg) GenerateAvatar(AvatarQuery query)
    {
        if (query.Format == AvatarFormat.Png) {
            var pngSize = query.Size ?? 80;
            var pngBytes = query.Kind switch {
                AvatarKind.Marble => MarbleAvatars.GeneratePngBytes(query.Key, pngSize, title: query.Title ?? ""),
                _ => BeamAvatars.GeneratePngBytes(query.Key, pngSize),
            };
            return (pngBytes, null);
        }

        var svg = query.Kind switch {
            AvatarKind.Marble => MarbleAvatars.GenerateSvg(query.Key, title: query.Title ?? ""),
            _ => BeamAvatars.GenerateSvg(query.Key),
        };
        return (null, svg);
    }

    private async Task CacheAvatar(FilePath filePath, byte[] content, CancellationToken cancellationToken)
    {
        try {
            EnsureCacheDir();
            await File.WriteAllBytesAsync(filePath, content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to cache avatar to '{Path}'", filePath);
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
