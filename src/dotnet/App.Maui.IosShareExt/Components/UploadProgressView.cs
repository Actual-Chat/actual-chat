using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class UploadProgressView(IosHub hub) : ComputedStateView<UploadProgressView.Model>(hub)
{
    private UIProgressView _progressBar = null!;
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Label
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = "Uploading...",
            Font = UIFont.SystemFontOfSize(24),
        };

        // Progress bar
        _progressBar = new UIProgressView(UIProgressViewStyle.Default) {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };

        // Cancel button
        var cancelButton = UIButton.FromType(UIButtonType.System);
        cancelButton.TranslatesAutoresizingMaskIntoConstraints = false;
        cancelButton.SetTitle("Cancel", UIControlState.Normal);
        cancelButton.TitleLabel.Font = UIFont.SystemFontOfSize(20, UIFontWeight.Semibold);
        cancelButton.TouchUpInside += Safe(ShareUI.CancelUploading);

        // Vertical stack view
        var stackView = new UIStackView([label, _progressBar, cancelButton]) {
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

            _progressBar.HeightAnchor.ConstraintEqualTo(8),
        ]);

        OnStateChanged(model);
    }

    protected override void OnStateChanged(Model model)
        => _progressBar.Progress = (float)(model.Progress / 100);

    protected override ComputedState<Model>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<Model>.Options {
                InitialValue = new Model(0),
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var progress = await ShareUI.UploadPct.Use(cancellationToken).ConfigureAwait(false);
        return new Model(progress);
    }

    // Nested types
    public record Model(double Progress);
}
