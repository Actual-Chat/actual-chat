using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Chat;

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
            BackgroundColor = UIColor.SystemBlue,
            Layer = { CornerRadius = 1.5f }
        };

        AddSubview(_iconView);
        AddSubview(_underlineView);

        NSLayoutConstraint.ActivateConstraints([
            _iconView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _iconView.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),

            _underlineView.TopAnchor.ConstraintEqualTo(_iconView.BottomAnchor, 4),
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
