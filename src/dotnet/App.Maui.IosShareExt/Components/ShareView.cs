using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ShareView : ComputedStateView<ShareView.Model>
{
    private const double FadeDuration = 0.3;
    private StatefulView? _stepView;
    private ShareStep? _displayedStep;
    private ShareUI ShareUI => Hub.ShareUI;

    public ShareView(IosHub hub) : base(hub)
    {
        // OnInitialRender waits on GetStep, and that waits on Accounts.GetOwn - a whole round trip
        // of blank sheet if the UI waits with it.
        BackgroundColor = AppColors.Background01;
        ShowContactSelection();
        _displayedStep = ShareStep.ContactSelection;
    }

    protected override void OnInitialRender(Model model)
        => OnStateChanged(model);

    protected override void OnStateChanged(Model model)
    {
        // None only means the suggested-recipient lookup is still running - the guest check is past.
        var step = model.Step == ShareStep.None ? ShareStep.ContactSelection : model.Step;
        if (step == _displayedStep)
            return;

        switch (step) {
            case ShareStep.SignIn:
                ShowSignIn();
                break;
            case ShareStep.ContactSelection:
                ShowContactSelection();
                break;
            case ShareStep.Uploading:
                ShowUploading();
                break;
            case ShareStep.Failed:
                ShowFailed();
                break;
            case ShareStep.Completed:
                ShowCompleted();
                break;
        }
        _displayedStep = step;
    }

    protected override ComputedState<Model>.Options GetStateOptions()
        => GetStateOptions(GetType(),
            static t => new ComputedState<Model>.Options {
                Category = GetStateCategory(t),
                UpdateDelayer = FixedDelayer.MinDelay,
            });

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var step = await ShareUI.GetStep(cancellationToken).ConfigureAwait(false);
        return new Model(step);
    }

    // Private methods

    private void ShowSignIn()
        => ShowStep(new SignInView(Hub));

    private void ShowContactSelection()
        => ShowStep(new ContactSelectionView(Hub));

    private void ShowUploading()
        => ShowStep(new UploadProgressView(Hub));

    private void ShowFailed()
        => ShowStep(new ErrorView(Hub));

    private void ShowCompleted()
        => ShowStep(new SuccessView(Hub));

    private void ShowStep(StatefulView view)
    {
        // Opaque, and set here rather than in the view's OnInitialRender, which runs too late to
        // cover what this replaces.
        view.BackgroundColor = AppColors.Background01;
        view.TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(view);

        NSLayoutConstraint.ActivateConstraints([
            view.TopAnchor.ConstraintEqualTo(TopAnchor),
            view.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            view.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            view.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        ]);

        var isFirst = _stepView is null;
        _stepView = view;
        if (isFirst)
            return;

        // Alpha, never a scale transform: Auto Layout recomputes the frame out from under a
        // non-identity one, so the outgoing view comes straight back.
        view.Alpha = 0;
        var animator = new UIViewPropertyAnimator(FadeDuration, UIViewAnimationCurve.EaseInOut, () => view.Alpha = 1);
        animator.AddCompletion(_ => RemoveStepViewsExcept(_stepView));
        animator.StartAnimation();
    }

    private void RemoveStepViewsExcept(UIView? keep)
    {
        // By subview, not by tracked field: an untracked step is exactly the one that would linger.
        foreach (var subview in Subviews) {
            if (!ReferenceEquals(subview, keep))
                subview.RemoveAndDisposeStates();
        }
    }

    // Nested types
    public record Model(ShareStep Step);
}
