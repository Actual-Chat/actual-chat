namespace ActualChat.App.Maui.IosShareExt.UI;

/// <summary>
/// The illustrations the extension bundles, each a light/dark pair behind a
/// <see cref="UIImageAsset"/> so UIKit swaps them with the appearance, exactly
/// as it would for an asset catalog entry.
/// </summary>
public static class AppImages
{
    public static UIImage? ErrorCat => field ??= Load("error-cat");
    public static UIImage? ShareCat => field ??= Load("share-cat");

    // Private methods

    private static UIImage? Load(string name)
    {
        var light = UIImage.FromBundle(name + "-light.png");
        var dark = UIImage.FromBundle(name + "-dark.png");
        if (light is null || dark is null)
            return light ?? dark;

        var lightTraits = UITraitCollection.FromUserInterfaceStyle(UIUserInterfaceStyle.Light);
        var asset = new UIImageAsset();
        asset.RegisterImage(light, lightTraits);
        asset.RegisterImage(dark, UITraitCollection.FromUserInterfaceStyle(UIUserInterfaceStyle.Dark));
        // Taking the image back from the asset is what carries the asset on it, and that's
        // what makes UIImageView re-resolve it when the appearance changes.
        return asset.FromTraitCollection(lightTraits);
    }
}
