namespace ActualChat.App.Maui.IosShareExt.UI;

/// <summary>
/// The illustrations the extension bundles, each a light/dark image set in
/// <c>Assets.xcassets</c> that UIKit re-resolves as the appearance changes.
/// </summary>
public static class AppImages
{
    public static UIImage? ErrorCat => field ??= UIImage.FromBundle("error-cat");
    public static UIImage? ShareCat => field ??= UIImage.FromBundle("share-cat");
}
