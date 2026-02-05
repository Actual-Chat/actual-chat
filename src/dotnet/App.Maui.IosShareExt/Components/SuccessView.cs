using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class SuccessView(IosHub hub) : ComputedStateView<SuccessView.Model>(hub)
{
    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        var iconContainer = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        // Semi-transparent white circle background (20% opacity like in SVG)
        var circleView = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.White.ColorWithAlpha(0.1f),
        };
        circleView.Layer.CornerRadius = 50; // Half of 100pt size
        iconContainer.AddSubview(circleView);

        // White checkmark on top
        var config = UIImageSymbolConfiguration.Create(48, UIImageSymbolWeight.Medium);
        var checkmarkImage = UIImage.GetSystemImage("checkmark", config);
        var checkmarkView = new UIImageView(checkmarkImage) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = UIColor.White,
        };
        iconContainer.AddSubview(checkmarkView);

        // Layout circle and checkmark within container
        NSLayoutConstraint.ActivateConstraints([
            circleView.LeadingAnchor.ConstraintEqualTo(iconContainer.LeadingAnchor),
            circleView.TrailingAnchor.ConstraintEqualTo(iconContainer.TrailingAnchor),
            circleView.TopAnchor.ConstraintEqualTo(iconContainer.TopAnchor),
            circleView.BottomAnchor.ConstraintEqualTo(iconContainer.BottomAnchor),

            checkmarkView.CenterXAnchor.ConstraintEqualTo(iconContainer.CenterXAnchor),
            checkmarkView.CenterYAnchor.ConstraintEqualTo(iconContainer.CenterYAnchor),
        ]);

        // Label
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = "Done!",
            Font = UIFont.SystemFontOfSize(24, UIFontWeight.Semibold),
            TextColor = UIColor.White,
        };

        // Vertical stack view
        var stackView = new UIStackView([iconContainer, label]) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Center,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 20,
        };
        AddSubview(stackView);

        // Layout constraints
        NSLayoutConstraint.ActivateConstraints([
            stackView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor, -25),
            stackView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 24),
            stackView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -24),

            iconContainer.HeightAnchor.ConstraintEqualTo(100),
            iconContainer.WidthAnchor.ConstraintEqualTo(100),
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
