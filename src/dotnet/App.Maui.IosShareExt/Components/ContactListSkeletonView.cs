using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

/// <summary>
/// Placeholder rows shown while <see cref="ContactListView"/> waits for its first result.
/// Laid out on <see cref="ContactView"/>'s geometry, so the real rows land where these sat.
/// </summary>
public sealed class ContactListSkeletonView : UIView
{
    private const int RowCount = 8;
    private const int IconLeading = 16;
    private const int TextLeading = IconLeading + ContactIconView.Size + 12;
    private const int TitleHeight = 12;
    private const int SubtitleHeight = 10;
    private const double PulseDuration = 0.9;
    private const float PulseAlpha = 0.35f;
    // Equal-length rows read as a table rather than as text, so the widths cycle instead
    private static readonly int[] TitleWidths = [140, 96, 168, 120, 152, 104, 132, 112];
    private static readonly int[] SubtitleWidths = [200, 148, 176, 128, 208, 160, 184, 136];

    private readonly UIView _rows;

    public ContactListSkeletonView()
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        // Opaque, and the pulse runs on _rows: fading this view would fade past its own background
        BackgroundColor = AppColors.Background01;
        UserInteractionEnabled = false;

        _rows = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = UIColor.Clear,
        };
        AddSubview(_rows);

        var constraints = new List<NSLayoutConstraint> {
            _rows.TopAnchor.ConstraintEqualTo(TopAnchor),
            _rows.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _rows.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _rows.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        };
        for (var i = 0; i < RowCount; i++) {
            var rowTop = i * ContactView.Height;
            var icon = AddBlock(ContactIconView.Size / 2f);
            var title = AddBlock(TitleHeight / 2f);
            var subtitle = AddBlock(SubtitleHeight / 2f);
            constraints.AddRange([
                icon.LeadingAnchor.ConstraintEqualTo(_rows.LeadingAnchor, IconLeading),
                icon.TopAnchor.ConstraintEqualTo(_rows.TopAnchor, rowTop + 6),
                icon.WidthAnchor.ConstraintEqualTo(ContactIconView.Size),
                icon.HeightAnchor.ConstraintEqualTo(ContactIconView.Size),

                title.LeadingAnchor.ConstraintEqualTo(_rows.LeadingAnchor, TextLeading),
                title.TopAnchor.ConstraintEqualTo(_rows.TopAnchor, rowTop + 11),
                title.WidthAnchor.ConstraintEqualTo(TitleWidths[i]),
                title.HeightAnchor.ConstraintEqualTo(TitleHeight),

                subtitle.LeadingAnchor.ConstraintEqualTo(_rows.LeadingAnchor, TextLeading),
                subtitle.TopAnchor.ConstraintEqualTo(_rows.TopAnchor, rowTop + 31),
                subtitle.WidthAnchor.ConstraintEqualTo(SubtitleWidths[i]),
                subtitle.HeightAnchor.ConstraintEqualTo(SubtitleHeight),
            ]);
        }
        NSLayoutConstraint.ActivateConstraints([..constraints]);
    }

    public override void MovedToWindow()
    {
        // UIKit drops animations added to a view that isn't in a window yet
        base.MovedToWindow();
        if (Window is not null)
            StartPulsing();
    }

    // Private methods

    private UIView AddBlock(float cornerRadius)
    {
        var block = new UIView {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = AppColors.Square,
            Layer = { CornerRadius = cornerRadius, MasksToBounds = true },
        };
        _rows.AddSubview(block);
        return block;
    }

    private void StartPulsing()
        => UIView.Animate(PulseDuration,
            0,
            UIViewAnimationOptions.Repeat
            | UIViewAnimationOptions.Autoreverse
            | UIViewAnimationOptions.CurveEaseInOut
            | UIViewAnimationOptions.AllowUserInteraction,
            () => _rows.Alpha = PulseAlpha,
            () => { });
}
