using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Contacts;
using ActualChat.Maui;
using ActualChat.Maui.Services;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ContactView(Contact contact, IosHub hub) : ComputedStateView<ContactView.Model>(hub)
{
    public const int Height = 52;
    private ContactIconView _iconView = null!;
    private UIImageView _checkbox = null!;
    private UILabel _titleLabel = null!;
    private UILabel _subtitleLabel = null!;

    private ShareUI ShareUI => field ??= Services.GetRequiredService<ShareUI>();
    private IChats Chats => Hub.Chats;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        BackgroundColor = UIColor.Clear;

        // Avatar
        _iconView = new ContactIconView(model.IconQuery, null, contact.Chat.Title, true, Hub);

        // Text container
        var textContainer = new UIStackView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 2,
            Alignment = UIStackViewAlignment.Leading,
        };

        // Title Label
        _titleLabel = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Font = UIFont.SystemFontOfSize(17, UIFontWeight.Regular),
            TextColor = AppColors.Text01,
            Text = contact.Chat.Title,
            Lines = 1,
            LineBreakMode = UILineBreakMode.TailTruncation,
        };

        // Subtitle Label
        _subtitleLabel = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Font = UIFont.SystemFontOfSize(14, UIFontWeight.Regular),
            TextColor = AppColors.Text03,
            Lines = 1,
            LineBreakMode = UILineBreakMode.TailTruncation,
            Text = model.LastMessage,
        };

        textContainer.AddArrangedSubview(_titleLabel);
        textContainer.AddArrangedSubview(_subtitleLabel);

        // Checkbox
        _checkbox = new UIImageView
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            ContentMode = UIViewContentMode.ScaleAspectFit,
            TintColor = AppColors.Text04,
        };
        _checkbox.SetContentCompressionResistancePriority((float)UILayoutPriority.Required, UILayoutConstraintAxis.Horizontal);
        SetIsSelected(model.IsSelected);

        AddSubview(_iconView);
        AddSubview(textContainer);
        AddSubview(_checkbox);

        // Fixed layout values to avoid any dependency on icon view anchors
        const int iconLeading = 16;
        const int textLeading = iconLeading + ContactIconView.Size + 12; // 16 + 40 + 12 = 68

        NSLayoutConstraint.ActivateConstraints([
            // Avatar
            _iconView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, iconLeading),
            _iconView.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _iconView.WidthAnchor.ConstraintEqualTo(ContactIconView.Size),
            _iconView.HeightAnchor.ConstraintEqualTo(ContactIconView.Size),

            // Text container - use fixed leading to avoid dependency on icon view
            textContainer.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, textLeading),
            textContainer.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            textContainer.TrailingAnchor.ConstraintLessThanOrEqualTo(_checkbox.LeadingAnchor, -12),

            // Checkbox (aligned to right edge)
            _checkbox.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -16),
            _checkbox.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _checkbox.WidthAnchor.ConstraintEqualTo(24),
            _checkbox.HeightAnchor.ConstraintEqualTo(24),

            // Size constraints
            HeightAnchor.ConstraintEqualTo(Height),
        ]);
    }

    private void SetIsSelected(bool isSelected)
    {
        if (isSelected) {
            _checkbox.Image = UIImage.GetSystemImage("checkmark.circle.fill");
            _checkbox.TintColor = AppColors.Primary;
        }
        else {
            _checkbox.Image = UIImage.GetSystemImage("circle");
            _checkbox.TintColor = AppColors.Text04;
        }
    }

    protected override void OnStateChanged(Model model)
    {
        _titleLabel.Text = model.Contact.Chat.Title;
        _subtitleLabel.Text = model.LastMessage;
        SetIsSelected(model.IsSelected);
    }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new () {
            UpdateDelayer = FixedDelayer.MinDelay,
            InitialValue = new Model(contact, false, "", contact.GetIconQuery()),
            Category = GetStateCategory(GetType()),
        };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var isContactSelected = await ShareUI.IsContactSelected(contact.Id, cancellationToken).ConfigureAwait(false);
        var chatNews = await Chats.GetNews(Hub.Session, contact.ChatId, cancellationToken).ConfigureAwait(false);
        var lastMessage = chatNews?.LastTextEntry?.Content ?? "";
        return new Model(contact, isContactSelected, lastMessage, contact.GetIconQuery());
    }

    // Nested types

    public sealed record Model(
        Contact Contact,
        bool IsSelected,
        string LastMessage,
        IconQuery IconQuery);
}
