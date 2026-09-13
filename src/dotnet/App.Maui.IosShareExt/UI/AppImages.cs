namespace ActualChat.App.Maui.IosShareExt.UI;

/// <summary>
/// The images the extension bundles as image sets in <c>Assets.xcassets</c>: illustrations
/// as light/dark pairs UIKit re-resolves as the appearance changes, and icon font glyphs
/// as template images that take the view's tint color.
/// </summary>
public static class AppImages
{
    public static UIImage? ErrorCat => field ??= UIImage.FromBundle("error-cat");
    public static UIImage? ShareCat => field ??= UIImage.FromBundle("share-cat");
    public static UIImage? MessageEllipse => field ??= UIImage.FromBundle("message-ellipse");
}
