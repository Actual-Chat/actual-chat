using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Server;

public sealed partial class AssetVersionHelper(ResourceAssetCollection assets)
{
    [GeneratedRegex(@"\.(?<hash>[a-z0-9]{10})\.(js|wasm)$")]
    private static partial Regex AssetVersionRegexFactory();
    private static readonly Regex AssetVersionRegex = AssetVersionRegexFactory();

    private readonly ConcurrentDictionary<string, string> _versionedPathCache = new(StringComparer.Ordinal);

    public ResourceAssetCollection Assets { get; } = assets;

    public string GetVersionedPath(string assetKey)
        => _versionedPathCache.GetOrAdd(assetKey,
            static (key, self) => {
                var assetPath = self.Assets[key];
                var match = AssetVersionRegex.Match(assetPath);
                if (match.Success) {
                    var version = match.Groups["hash"].Value;
                    return key + "?v=" + version;
                }
                return assetPath;
            }, this);
}
