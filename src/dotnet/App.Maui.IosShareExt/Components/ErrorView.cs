using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class ErrorView(IosHub hub) : ComputedStateView<ErrorView.Model>(hub)
{
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Label
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = model.Message,
            Font = UIFont.SystemFontOfSize(24),
            TextColor = UIColor.White,
        };

        // Image
        var imageView = new UIImageView(UIImage.FromBundle("error-barrier-image.png")) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
        };

        // Close button
        var closeButton = UIButton.FromType(UIButtonType.System);
        closeButton.TranslatesAutoresizingMaskIntoConstraints = false;
        closeButton.SetTitle("Close", UIControlState.Normal);
        closeButton.TitleLabel.Font = UIFont.SystemFontOfSize(20, UIFontWeight.Semibold);
        closeButton.TouchUpInside += Safe(ct => ShareUI.CloseApp(ct));

        // Vertical stack view
        var stackView = new UIStackView([imageView, label, closeButton]) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 20,
        };
        AddSubview(stackView);

        // Layout constraints
        NSLayoutConstraint.ActivateConstraints([
            stackView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor, -25),
            stackView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 24),
            stackView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -24),

            imageView.HeightAnchor.ConstraintEqualTo(164),
        ]);
    }

    protected override void OnStateChanged(Model model)
    {
    }

    protected override Task<Model> ComputeState(CancellationToken cancellationToken)
        => Task.FromResult(new Model("Upload failed"));

    // Nested types
    public sealed record Model(string Message);
}
