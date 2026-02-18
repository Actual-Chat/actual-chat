using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class SignInView(IosHub hub) : ComputedStateView<SignInView.Model>(hub)
{
    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Icon
        var iconContainer = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        var circleView = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.White.ColorWithAlpha(0.1f),
        };
        circleView.Layer.CornerRadius = 50;
        iconContainer.AddSubview(circleView);

        var config = UIImageSymbolConfiguration.Create(48, UIImageSymbolWeight.Medium);
        var lockImage = UIImage.GetSystemImage("lock.fill", config);
        var lockView = new UIImageView(lockImage) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = UIColor.White,
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
            Text = "Sign in to Voxt to share content",
            Font = UIFont.SystemFontOfSize(20),
            TextColor = UIColor.White,
        };

        // Sign In button
        var signInButton = UIButton.FromType(UIButtonType.System);
        signInButton.TranslatesAutoresizingMaskIntoConstraints = false;
        signInButton.SetTitle("Sign In", UIControlState.Normal);
        signInButton.TitleLabel.Font = UIFont.SystemFontOfSize(18, UIFontWeight.Semibold);
        signInButton.SetTitleColor(UIColor.White, UIControlState.Normal);
        signInButton.BackgroundColor = new UIColor(red: 0.27f, green: 0.42f, blue: 1.0f, alpha: 1.0f);
        signInButton.Layer.CornerRadius = 12;
        signInButton.TouchUpInside += Safe(OpenMainApp);

        // Close button
        var closeButton = UIButton.FromType(UIButtonType.System);
        closeButton.TranslatesAutoresizingMaskIntoConstraints = false;
        closeButton.SetTitle("Close", UIControlState.Normal);
        closeButton.TitleLabel.Font = UIFont.SystemFontOfSize(16);
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

    private async Task OpenMainApp(CancellationToken cancellationToken)
    {
        var url = new NSUrl("voxt://");
        await UIKitExt.OpenUrl(url).ConfigureAwait(false);
        await UIKitExt.CloseApp(cancellationToken).ConfigureAwait(false);
    }

    // Nested types
    public sealed record Model;
}
