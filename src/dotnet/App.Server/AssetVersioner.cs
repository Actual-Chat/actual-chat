using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Server;

public partial class AssetVersioner(ResourceAssetCollection assets)
{
    [GeneratedRegex(@"\.(?<hash>[a-z0-9]{10})\.(js|wasm)$")]
    private static partial Regex AssetVersionRegexFactory();
    private static readonly Regex AssetVersionRegex = AssetVersionRegexFactory();

    public ResourceAssetCollection Assets { get; } = assets;

    public string GetVersionedAsset(string assetPath) {
        var mapping = Assets[assetPath];
        var match = AssetVersionRegex.Match(mapping);
        if (match.Success) {
            var version = match.Groups["hash"].Value;
            return assetPath + "?v=" + version;
        }

        return assetPath;
    }
}
