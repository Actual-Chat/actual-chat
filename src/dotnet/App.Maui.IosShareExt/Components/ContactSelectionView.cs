using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ContactSelectionView(IosHub hub) : ComputedStateView<ContactSelectionView.Model?>(hub)
{
    private ShareUI ShareUI => field ??= Services.GetRequiredService<ShareUI>();
    private UIButton _sendButton = null!;
    private CounterBadgeView _sendBadge = null!;
    private NSLayoutConstraint _commentBottomConstraint = null!;
    private NSObject? _keyboardShowObserver;
    private NSObject? _keyboardHideObserver;

    protected override void OnInitialRender(Model? model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        // Close button
        var closeButton = new UIButton(UIButtonType.System);
        closeButton.TranslatesAutoresizingMaskIntoConstraints = false;
        closeButton.SetImage(UIImage.GetSystemImage("xmark"), UIControlState.Normal);
        closeButton.TintColor = UIColor.White;
        closeButton.TouchUpInside += Safe(UIKitExt.CloseApp);
        AddSubview(closeButton);

        // Title label
        var titleLabel = new UILabel
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Text = "Share with",
            Font = UIFont.SystemFontOfSize(17, UIFontWeight.Semibold)!,
            TextColor = UIColor.White,
            TextAlignment = UITextAlignment.Center
        };
        AddSubview(titleLabel);

        // Send button
        _sendButton = new UIButton(UIButtonType.System);
        _sendButton.TranslatesAutoresizingMaskIntoConstraints = false;
        _sendButton.SetTitle("Send", UIControlState.Normal);
        _sendButton.TitleLabel.Font = UIFont.SystemFontOfSize(17, UIFontWeight.Semibold)!;
        _sendButton.TouchUpInside += Safe(() => ShareUI.StartSending());
        AddSubview(_sendButton);

        // Selected contact counter, sitting left of the send button
        _sendBadge = new CounterBadgeView();
        AddSubview(_sendBadge);
        UpdateSendButton(model);

        // Search field
        var searchField = new UITextField
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Placeholder = "Who would you like to share with",
            Font = UIFont.SystemFontOfSize(17),
            TextColor = UIColor.White,
            BackgroundColor = new UIColor(red: 0.18f, green: 0.18f, blue: 0.19f, alpha: 1.0f),
            LeftView = new UIView(new CGRect(0, 0, 44, 44)),
            LeftViewMode = UITextFieldViewMode.Always,
            ReturnKeyType = UIReturnKeyType.Search,
        };
        searchField.Layer.CornerRadius = 22;
        searchField.ClipsToBounds = true;
        searchField.EditingChanged += Safe(() => ShareUI.SetFilter(searchField.Text ?? ""));
        searchField.ShouldReturn = textField => {
            textField.ResignFirstResponder();
            return true;
        };

        // Add search icon to the left view with proper centering
        var searchIcon = new UIImageView(UIImage.GetSystemImage("magnifyingglass"))
        {
            Frame = new CGRect(10, 10, 20, 20),
            TintColor = UIColor.LightGray,
            ContentMode = UIViewContentMode.ScaleAspectFit
        };
        searchField.LeftView.AddSubview(searchIcon);
        AddSubview(searchField);

        // Place list
        var placeListView = new PlaceListView(Hub);
        AddSubview(placeListView);

        // Contact list
        var contactListView = new ContactListView(Hub);
        AddSubview(contactListView);

        // Comment field
        var commentField = new UITextField
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            Placeholder = "Add your comment (optional)",
            Font = UIFont.SystemFontOfSize(17),
            TextColor = UIColor.White,
            BackgroundColor = new UIColor(red: 0.18f, green: 0.18f, blue: 0.19f, alpha: 1.0f),
            LeftView = new UIView(new CGRect(0, 0, 44, 44)),
            LeftViewMode = UITextFieldViewMode.Always,
            ReturnKeyType = UIReturnKeyType.Default,
        };
        commentField.Layer.CornerRadius = 22;
        commentField.ClipsToBounds = true;
        commentField.EditingChanged += Safe(() => ShareUI.SetComment(commentField.Text ?? ""));
        commentField.ShouldReturn = textField => {
            textField.ResignFirstResponder();
            return true;
        };

        // Add comment icon to the left view with proper centering
        var commentIcon = new UIImageView(UIImage.GetSystemImage("message"))
        {
            Frame = new CGRect(12, 12, 20, 20),
            TintColor = UIColor.LightGray,
            ContentMode = UIViewContentMode.ScaleAspectFit
        };
        commentField.LeftView.AddSubview(commentIcon);
        AddSubview(commentField);
        _commentBottomConstraint = commentField.BottomAnchor.ConstraintEqualTo(SafeAreaLayoutGuide.BottomAnchor, -8);
        ObserveKeyboard();

        // Add tap gesture to dismiss keyboard when tapping outside
        var tapGesture = new UITapGestureRecognizer(() => {
            searchField.ResignFirstResponder();
            commentField.ResignFirstResponder();
        });
        tapGesture.CancelsTouchesInView = false;
        AddGestureRecognizer(tapGesture);

        NSLayoutConstraint.ActivateConstraints([
            closeButton.TopAnchor.ConstraintEqualTo(SafeAreaLayoutGuide.TopAnchor, 12),
            closeButton.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 8),
            closeButton.WidthAnchor.ConstraintEqualTo(44),
            closeButton.HeightAnchor.ConstraintEqualTo(44),

            titleLabel.CenterYAnchor.ConstraintEqualTo(closeButton.CenterYAnchor),
            titleLabel.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),

            _sendButton.CenterYAnchor.ConstraintEqualTo(closeButton.CenterYAnchor),
            _sendButton.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -16),

            _sendBadge.TrailingAnchor.ConstraintEqualTo(_sendButton.LeadingAnchor, -6),
            _sendBadge.CenterYAnchor.ConstraintEqualTo(_sendButton.CenterYAnchor),

            searchField.TopAnchor.ConstraintEqualTo(closeButton.BottomAnchor, 8),
            searchField.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 16),
            searchField.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -16),
            searchField.HeightAnchor.ConstraintEqualTo(44),

            placeListView.TopAnchor.ConstraintEqualTo(searchField.BottomAnchor, 12),
            placeListView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            placeListView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),

            contactListView.TopAnchor.ConstraintEqualTo(placeListView.BottomAnchor, 16),
            contactListView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            contactListView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            contactListView.BottomAnchor.ConstraintEqualTo(commentField.TopAnchor, -8),

            commentField.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 16),
            commentField.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -16),
            commentField.HeightAnchor.ConstraintEqualTo(44),
            _commentBottomConstraint,
        ]);
    }

    private void ObserveKeyboard()
    {
        _keyboardShowObserver = UIKeyboard.Notifications.ObserveWillShow((_, e) => {
            var keyboardHeight = e.FrameEnd.Height;
            _commentBottomConstraint.Constant = -(keyboardHeight - SafeAreaInsets.Bottom + 8);
            UIView.Animate(e.AnimationDuration, LayoutIfNeeded);
        });
        _keyboardHideObserver = UIKeyboard.Notifications.ObserveWillHide((_, e) => {
            _commentBottomConstraint.Constant = -8;
            UIView.Animate(e.AnimationDuration, LayoutIfNeeded);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _keyboardShowObserver?.Dispose();
            _keyboardHideObserver?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnStateChanged(Model? model)
        => UpdateSendButton(model);

    private void UpdateSendButton(Model? model)
    {
        var selectedCount = model?.SelectedCount ?? 0;
        _sendButton.Enabled = selectedCount > 0;
        _sendBadge.Count = selectedCount;
    }

    protected override ComputedState<Model?>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<Model?>.Options {
                InitialValue = null,
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    protected override async Task<Model?> ComputeState(CancellationToken cancellationToken)
    {
        var selectedCount = await ShareUI.GetSelectedCount(cancellationToken).ConfigureAwait(false);
        return new Model(selectedCount);
    }

    // Nested types

    public sealed record Model(int SelectedCount);
}
