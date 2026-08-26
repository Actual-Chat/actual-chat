using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Localization;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class UploadProgressView(IosHub hub) : ComputedStateView<UploadProgressView.Model>(hub)
{
    // The design pins the bar to the ramp rather than to --primary: blue/70 in both themes,
    // on an ash/90 track in light and an ash/30 one in dark.
    private static readonly UIColor ProgressColor = AppColors.Blue70;
    private static readonly UIColor ProgressTrackColor = AppColors.Dynamic(AppColors.Ash90, AppColors.Ash30);
    private UIProgressView _progressBar = null!;
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Image
        var imageView = new UIImageView(AppImages.ShareCat) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
        };
        // The stack stretches its rows to full width, so the image keeps its own size inside this container
        var imageContainer = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        imageContainer.AddSubview(imageView);

        // Progress bar
        _progressBar = new UIProgressView(UIProgressViewStyle.Default) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ProgressTintColor = ProgressColor,
            TrackTintColor = ProgressTrackColor,
            ClipsToBounds = true,
        };
        _progressBar.Layer.CornerRadius = 2;

        // Label
        var label = new UILabel {
            TranslatesAutoresizingMaskIntoConstraints = false,
            TextAlignment = UITextAlignment.Center,
            Text = AppStrings.L.ShareExt_Uploading,
            Font = UIFont.SystemFontOfSize(16, UIFontWeight.Medium)!,
            TextColor = AppColors.Text01,
        };

        // Vertical stack view
        var stackView = new UIStackView([imageContainer, _progressBar, label]) {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Fill,
            Distribution = UIStackViewDistribution.Fill,
            Spacing = 28,
        };
        stackView.SetCustomSpacing(17, _progressBar);
        AddSubview(stackView);

        // Cancel button
        var cancelConfiguration = UIButtonConfiguration.PlainButtonConfiguration;
        cancelConfiguration.ContentInsets = new NSDirectionalEdgeInsets(10, 24, 10, 24);
        cancelConfiguration.BaseForegroundColor = AppColors.Primary;
        cancelConfiguration.AttributedTitle = new NSAttributedString(AppStrings.L.Common_Cancel,
            new UIStringAttributes { Font = UIFont.SystemFontOfSize(16, UIFontWeight.Medium) });
        var cancelButton = new UIButton {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Configuration = cancelConfiguration,
        };
        cancelButton.TouchUpInside += Safe(ShareUI.CancelUploading);
        AddSubview(cancelButton);

        // Layout constraints
        NSLayoutConstraint.ActivateConstraints([
            stackView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor, -49),
            stackView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 31),
            stackView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -31),

            imageContainer.HeightAnchor.ConstraintEqualTo(200),
            imageView.WidthAnchor.ConstraintEqualTo(210),
            imageView.HeightAnchor.ConstraintEqualTo(200),
            imageView.CenterXAnchor.ConstraintEqualTo(imageContainer.CenterXAnchor),
            imageView.CenterYAnchor.ConstraintEqualTo(imageContainer.CenterYAnchor),

            _progressBar.HeightAnchor.ConstraintEqualTo(4),

            cancelButton.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -16),
            cancelButton.BottomAnchor.ConstraintEqualTo(SafeAreaLayoutGuide.BottomAnchor, -16),
            cancelButton.HeightAnchor.ConstraintEqualTo(40),
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
