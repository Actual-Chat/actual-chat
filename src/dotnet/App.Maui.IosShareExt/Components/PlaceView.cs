using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Maui;
using ActualChat.Maui.Services;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class PlaceView(Place? place, IosHub hub) : ComputedStateView<bool>(hub)
{
    private ContactIconView? _iconView;
    private UIView _underlineView = null!;

    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(bool isSelected)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Icon view
        _iconView = new ContactIconView(place?.GetIconQuery(), UIImage.GetSystemImage("message"), place?.Title ?? "", false, Hub);

        // Underline indicator
        _underlineView = new UIView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            BackgroundColor = AppColors.Primary,
            Layer = { CornerRadius = 1.5f }
        };

        AddSubview(_iconView);
        AddSubview(_underlineView);

        // Fixed underline position to avoid dependency on icon view anchors
        const int underlineTop = ContactIconView.Size + 4; // 40 + 4 = 44

        NSLayoutConstraint.ActivateConstraints([
            _iconView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _iconView.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),
            _iconView.WidthAnchor.ConstraintEqualTo(ContactIconView.Size),
            _iconView.HeightAnchor.ConstraintEqualTo(ContactIconView.Size),

            _underlineView.TopAnchor.ConstraintEqualTo(TopAnchor, underlineTop),
            _underlineView.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),
            _underlineView.WidthAnchor.ConstraintEqualTo(32),
            _underlineView.HeightAnchor.ConstraintEqualTo(3),
        ]);

        SetIsSelected(isSelected);
    }

    protected override ComputedState<bool>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<bool>.Options {
                InitialValue = false,
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    protected override void OnStateChanged(bool isSelected)
        => SetIsSelected(isSelected);

    private void SetIsSelected(bool isSelected)
        => _underlineView.Hidden = !isSelected;

    protected override async Task<bool> ComputeState(CancellationToken cancellationToken)
    {
        var selectedPlaceId = await ShareUI.SelectedPlaceId.Use(cancellationToken).ConfigureAwait(false);
        return selectedPlaceId == place?.Id;
    }
}
