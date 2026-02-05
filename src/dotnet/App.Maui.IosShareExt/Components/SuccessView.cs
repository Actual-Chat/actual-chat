using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class SuccessView(IosHub hub) : ComputedStateView<SuccessView.Model>(hub)
{
    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Checkmark icon using SF Symbols
        var config = UIImageSymbolConfiguration.Create(80, UIImageSymbolWeight.Medium);
        var checkmarkImage = UIImage.GetSystemImage("checkmark.circle.fill", config);
        var imageView = new UIImageView(checkmarkImage) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = new UIColor(red: 0.298f, green: 0.686f, blue: 0.314f, alpha: 1.0f), // Green color
        };

        // Label
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = "Done!",
            Font = UIFont.SystemFontOfSize(24, UIFontWeight.Semibold),
            TextColor = UIColor.White,
        };

        // Vertical stack view
        var stackView = new UIStackView([imageView, label]) {
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

            imageView.HeightAnchor.ConstraintEqualTo(100),
            imageView.WidthAnchor.ConstraintEqualTo(100),
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
