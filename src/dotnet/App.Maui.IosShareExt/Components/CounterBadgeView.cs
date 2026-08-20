using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

/// <summary>
/// A pill-shaped counter label that hides itself when <see cref="Count"/> is zero or less.
/// </summary>
public sealed class CounterBadgeView : UILabel
{
    private const int MinSize = 18;
    private const int PaddingX = 5;

    public int Count {
        get;
        set {
            if (field == value)
                return;

            field = value;
            Text = value.Format();
            Hidden = value <= 0;
            InvalidateIntrinsicContentSize();
        }
    }

    public override CGSize IntrinsicContentSize {
        get {
            var size = base.IntrinsicContentSize;
            return new CGSize(Math.Max(MinSize, size.Width + (2 * PaddingX)), MinSize);
        }
    }

    public CounterBadgeView()
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        Font = UIFont.SystemFontOfSize(12, UIFontWeight.Semibold)!;
        TextColor = AppColors.PrimaryTitle;
        TextAlignment = UITextAlignment.Center;
        BackgroundColor = AppColors.Primary;
        Layer.CornerRadius = MinSize / 2f;
        ClipsToBounds = true;
        UserInteractionEnabled = false;
        Hidden = true;
    }
}
