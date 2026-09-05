using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Maui;
using ActualChat.Maui.Services;
using ActualChat.UI;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ContactIconView(IconQuery? iconQuery, UIImage? defaultImage, string title, bool isRound, IosHub hub)
    : ComputedStateView<LoadedImage?>(hub)
{
    public const int Size = 40;
    private UIImageView _image = null!;
    private UILabel _initialLabel = null!;

    private IconUI IconUI => Hub.IconUI;
    private int CornerRadius => isRound ? Size / 2 : 12;

    protected override void OnInitialRender(LoadedImage? imageData)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Avatar - circular
        _image = new UIImageView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFill,
            BackgroundColor = AppColors.Primary,
            TintColor = AppColors.Primary,
            Layer = { CornerRadius = CornerRadius, MasksToBounds = true },
        };

        // Add initial letter if no image
        _initialLabel = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Font = UIFont.SystemFontOfSize(Size / 2f, UIFontWeight.Medium)!,
            TextColor = AppColors.PrimaryTitle,
            TextAlignment = UITextAlignment.Center,
            Text = title.Length > 0 ? title[0].ToString().ToUpper() : string.Empty,
        };
        _image.AddSubview(_initialLabel);
        SetImage(imageData);

        AddSubview(_image);

        NSLayoutConstraint.ActivateConstraints([
            _image.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),
            _image.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _image.WidthAnchor.ConstraintEqualTo(Size),
            _image.HeightAnchor.ConstraintEqualTo(Size),

            _initialLabel.CenterXAnchor.ConstraintEqualTo(_image.CenterXAnchor),
            _initialLabel.CenterYAnchor.ConstraintEqualTo(_image.CenterYAnchor),
        ]);
    }

    protected override void OnStateChanged(LoadedImage? model)
        => SetImage(model);

    private void SetImage(LoadedImage? model)
    {
        var image = model?.FilePath.IsEmpty != false
            ? defaultImage
            : UIImage.FromFile(model.FilePath);
        var old = _image.Image;
        if (image is null) {
            _image.BackgroundColor = AppColors.Primary;
            _image.Image = null;
            _initialLabel.Hidden = false;
        }
        else {
            _image.BackgroundColor = UIColor.Clear;
            _image.Image = image;
            _initialLabel.Hidden = model?.AvatarKind is not AvatarKind.Marble;
        }
        if (!ReferenceEquals(old, image))
            old.DisposeSilently();
    }

    protected override Task<LoadedImage?> ComputeState(CancellationToken cancellationToken)
        => iconQuery is not null ? IconUI.Get(iconQuery, cancellationToken) : Task.FromResult<LoadedImage?>(null);
}
