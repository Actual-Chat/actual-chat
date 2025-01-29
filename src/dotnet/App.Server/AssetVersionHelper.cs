using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace ActualChat.App.Server;

public static partial class AssetVersionHelper
{
    [GeneratedRegex(@"\.(?<hash>[a-z0-9]{10})\.(js|wasm)$")]
    private static partial Regex AssetVersionRegexFactory();
    private static readonly Regex AssetVersionRegex = AssetVersionRegexFactory();

    public static string? GetVersion(string assetPath)
    {
        var match = AssetVersionRegex.Match(assetPath);
        return match.Success
            ? match.Groups["hash"].Value
            : null;
    }

    public static ImportMapDefinition StripRelativePaths(ImportMapDefinition importMap)
    {
        var assetMap = new Dictionary<string, string>();
        var integrityMap = new Dictionary<string, string>();
        if (importMap.Imports != null)
            foreach (var (key, value) in importMap.Imports)
                if (key.StartsWith("./", StringComparison.Ordinal))
                    assetMap[key[1..]] = value[1..];
                else
                    assetMap[key] = value;
        if (importMap.Integrity != null)
            foreach (var (key, value) in importMap.Integrity)
                if (key.StartsWith("./", StringComparison.Ordinal))
                    integrityMap[key[1..]] = value;
                else
                    integrityMap[key] = value;
        return new(assetMap, importMap.Scopes, integrityMap);
    }

    public static ImportMapDefinition GetWasmAssetsImportMap(ResourceAssetCollection assets)
    {
        var assetMap = new Dictionary<string, string>();
        var integrityMap = new Dictionary<string, string>();
        foreach (var asset in assets) {
            if (!asset.Url.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) ||
                !asset.Url.StartsWith("dist", StringComparison.OrdinalIgnoreCase) ||
                asset.Properties == null)
                continue;

            var (integrity, label) = GetAssetProperties(asset);
            if (integrity != null)
                integrityMap[$"./{asset.Url}"] = integrity;
            if (label != null)
                assetMap[$"./{label}"] = $"./{asset.Url}";
        }
        return new(assetMap, new Dictionary<string, IReadOnlyDictionary<string, string>>(), integrityMap);
    }

    private static (string? integrity, string? label) GetAssetProperties(ResourceAsset asset) {
        string? integrity = null;
        string? label = null;
        for (var i = 0; i < asset.Properties!.Count; i++) {
            var property = asset.Properties[i];
            if (string.Equals(property.Name, "integrity", StringComparison.OrdinalIgnoreCase))
                integrity = property.Value;
            else if (string.Equals(property.Name, "label", StringComparison.OrdinalIgnoreCase))
                label = property.Value;

            if (integrity != null && label != null)
                return (integrity, label);
        }
        return (integrity, label);
    }
}
