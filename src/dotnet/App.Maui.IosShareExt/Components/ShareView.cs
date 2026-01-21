using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.App.Maui.IosShareExt.Services;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class ShareView(IosHub hub) : ComputedStateView<ShareView.Model>(hub)
{
    private ShareUI ShareUI => Hub.ShareUI;
    private ContactSelectionView? _contactSelectionView;
    private UploadProgressView? _uploadProgressView;
    private ErrorView? _errorView;
    private bool _isUploadingDisplayed;
    private bool _isFailedDisplayed;

    protected override void OnInitialRender(Model model)
    {
        BackgroundColor = new UIColor(red: 0.11f, green: 0.11f, blue: 0.12f, alpha: 1.0f);

        _contactSelectionView = new ContactSelectionView(Hub);
        _contactSelectionView.TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(_contactSelectionView);

        NSLayoutConstraint.ActivateConstraints([
            _contactSelectionView.TopAnchor.ConstraintEqualTo(TopAnchor),
            _contactSelectionView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _contactSelectionView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _contactSelectionView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        ]);

        OnStateChanged(model);
    }

    protected override void OnStateChanged(Model model)
    {
        if (model.IsFailed && !_isFailedDisplayed) {
            _errorView = new ErrorView(Hub);
            _errorView.TranslatesAutoresizingMaskIntoConstraints = false;
            _errorView.Alpha = 0;
            AddSubview(_errorView);

            NSLayoutConstraint.ActivateConstraints([
                _errorView.TopAnchor.ConstraintEqualTo(TopAnchor),
                _errorView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                _errorView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
                _errorView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            ]);

            var animator = new UIViewPropertyAnimator(0.3,
                UIViewAnimationCurve.EaseInOut,
                () => {
                    if (_contactSelectionView != null)
                        _contactSelectionView.Transform = CGAffineTransform.MakeScale(0.0f, 0.0f);
                    if (_uploadProgressView != null)
                        _uploadProgressView.Transform = CGAffineTransform.MakeScale(0.0f, 0.0f);
                    _errorView!.Transform = CGAffineTransform.MakeScale(1.0f, 1.0f);
                    _errorView.Alpha = 1;
                });
            animator.AddCompletion(_ => {
                _contactSelectionView?.RemoveFromSuperview();
                _uploadProgressView?.RemoveFromSuperview();
            });
            animator.StartAnimation();

            _isFailedDisplayed = true;
            return;
        }

        if (model.IsUploading && !_isUploadingDisplayed) {
            _uploadProgressView = new UploadProgressView(Hub);
            _uploadProgressView.TranslatesAutoresizingMaskIntoConstraints = false;
            _uploadProgressView.Alpha = 0;
            AddSubview(_uploadProgressView);

            NSLayoutConstraint.ActivateConstraints([
                _uploadProgressView.TopAnchor.ConstraintEqualTo(TopAnchor),
                _uploadProgressView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                _uploadProgressView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
                _uploadProgressView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
            ]);

            var animator = new UIViewPropertyAnimator(0.3,
                UIViewAnimationCurve.EaseInOut,
                () => {
                    _contactSelectionView!.Transform = CGAffineTransform.MakeScale(0.0f, 0.0f);
                    _uploadProgressView!.Transform = CGAffineTransform.MakeScale(1.0f, 1.0f);
                    _uploadProgressView.Alpha = 1;
                });
            animator.AddCompletion(_ => {
                _contactSelectionView?.RemoveFromSuperview();
            });
            animator.StartAnimation();

            _isUploadingDisplayed = true;
        }
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var isUploading = await ShareUI.IsUploading.Use(cancellationToken).ConfigureAwait(false);
        var isFailed = await ShareUI.IsFailed.Use(cancellationToken).ConfigureAwait(false);
        return new Model(isUploading, isFailed);
    }

    // Nested types
    public record Model(bool IsUploading, bool IsFailed);
}
