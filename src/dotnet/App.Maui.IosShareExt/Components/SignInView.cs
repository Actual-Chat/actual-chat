using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Localization;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class SignInView(IosHub hub) : ComputedStateView<SignInView.Model>(hub)
{
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Icon
        var iconContainer = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        var circleView = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = AppColors.Square,
        };
        circleView.Layer.CornerRadius = 50;
        iconContainer.AddSubview(circleView);

        var config = UIImageSymbolConfiguration.Create(48, UIImageSymbolWeight.Medium);
        var lockImage = UIImage.GetSystemImage("lock.fill", config);
        var lockView = new UIImageView(lockImage) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = AppColors.Text01,
        };
        iconContainer.AddSubview(lockView);

        NSLayoutConstraint.ActivateConstraints([
            circleView.LeadingAnchor.ConstraintEqualTo(iconContainer.LeadingAnchor),
            circleView.TrailingAnchor.ConstraintEqualTo(iconContainer.TrailingAnchor),
            circleView.TopAnchor.ConstraintEqualTo(iconContainer.TopAnchor),
            circleView.BottomAnchor.ConstraintEqualTo(iconContainer.BottomAnchor),

            lockView.CenterXAnchor.ConstraintEqualTo(iconContainer.CenterXAnchor),
            lockView.CenterYAnchor.ConstraintEqualTo(iconContainer.CenterYAnchor),
        ]);

        // Message
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = L.ShareExt_SignInPrompt_Format(CoreConstants.AppName),
            Font = UIFont.SystemFontOfSize(20),
            TextColor = AppColors.Text01,
        };

        // Sign In button
        var signInButton = UIButton.FromType(UIButtonType.System);
        signInButton.TranslatesAutoresizingMaskIntoConstraints = false;
        signInButton.SetTitle(L.SignIn_SignIn, UIControlState.Normal);
        signInButton.TitleLabel.Font = UIFont.SystemFontOfSize(18, UIFontWeight.Semibold);
        signInButton.SetTitleColor(AppColors.PrimaryTitle, UIControlState.Normal);
        signInButton.BackgroundColor = AppColors.Primary;
        signInButton.Layer.CornerRadius = 12;
        signInButton.TouchUpInside += Safe(ShareUI.OpenMainApp);

        // Close button
        var closeButton = UIButton.FromType(UIButtonType.System);
        closeButton.TranslatesAutoresizingMaskIntoConstraints = false;
        closeButton.SetTitle(L.Common_Close, UIControlState.Normal);
        closeButton.TitleLabel.Font = UIFont.SystemFontOfSize(16);
        closeButton.TintColor = AppColors.Primary;
        closeButton.TouchUpInside += Safe(UIKitExt.CloseApp);

        // Vertical stack
        var stackView = new UIStackView([iconContainer, label, signInButton, closeButton]) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 20,
        };
        AddSubview(stackView);

        NSLayoutConstraint.ActivateConstraints([
            stackView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor, -25),
            stackView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 24),
            stackView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -24),

            iconContainer.HeightAnchor.ConstraintEqualTo(100),
            iconContainer.WidthAnchor.ConstraintEqualTo(100),

            signInButton.HeightAnchor.ConstraintEqualTo(48),
            signInButton.LeadingAnchor.ConstraintEqualTo(stackView.LeadingAnchor, 24),
            signInButton.TrailingAnchor.ConstraintEqualTo(stackView.TrailingAnchor, -24),
        ]);
    }

    protected override void OnStateChanged(Model model)
    {
    }

    protected override Task<Model> ComputeState(CancellationToken cancellationToken)
        => Task.FromResult(new Model());

    // Nested types
    public sealed record Model;
}
