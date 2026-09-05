using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.Localization;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

/// <summary>
/// The "something broke" screen - cat, message, Close. Shared by <see cref="ErrorView"/> and by
/// <see cref="ShareViewController"/>, which can't use ErrorView itself because it needs DI.
/// </summary>
public sealed class ErrorContentView : UIView
{
    public ErrorContentView(string message, EventHandler onClose)
    {
        BackgroundColor = AppColors.Background01;

        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = message,
            Font = UIFont.SystemFontOfSize(24)!,
            TextColor = AppColors.Text01,
            Lines = 0,
        };
        var imageView = new UIImageView(AppImages.ErrorCat) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
        };
        var closeButton = UIButton.FromType(UIButtonType.System);
        closeButton.TranslatesAutoresizingMaskIntoConstraints = false;
        closeButton.SetTitle(AppStrings.L.Common_Close, UIControlState.Normal);
        closeButton.TitleLabel.Font = UIFont.SystemFontOfSize(20, UIFontWeight.Semibold)!;
        closeButton.TintColor = AppColors.Primary;
        closeButton.TouchUpInside += onClose;

        var stackView = new UIStackView([imageView, label, closeButton]) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 20,
        };
        AddSubview(stackView);

        NSLayoutConstraint.ActivateConstraints([
            stackView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor, -25),
            stackView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 24),
            stackView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -24),

            imageView.HeightAnchor.ConstraintEqualTo(164),
        ]);
    }
}
