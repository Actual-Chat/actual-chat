using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.Maui;
using CoreFoundation;

namespace ActualChat.App.Maui.IosShareExt.Components;

public sealed class ShareView : ComputedStateView<ShareView.Model>
{
    private const double FadeDuration = 0.3;
    private static readonly TimeSpan OptimisticStepDelay = TimeSpan.FromMilliseconds(250);

    private StatefulView? _stepView;
    private ShareStep? _displayedStep;
    private ShareUI ShareUI => Hub.ShareUI;

    public ShareView(IosHub hub) : base(hub)
    {
        // GetStep waits on Accounts.GetOwn, which the computed cache now answers - so the sheet is
        // better off waiting a beat than painting a contact list a guest can't use. The optimistic
        // paint stays as the cold-cache fallback, where that wait really is a round trip, and the
        // delay is short enough to sit inside the share sheet's own presentation animation.
        BackgroundColor = AppColors.Background01;
        DispatchQueue.MainQueue.DispatchAfter(new DispatchTime(DispatchTime.Now, OptimisticStepDelay), () => {
            if (!IsDisposed && _displayedStep is null)
                ShowStep(ShareStep.ContactSelection);
        });
    }

    protected override void OnInitialRender(Model model)
        => OnStateChanged(model);

    protected override void OnStateChanged(Model model)
        // None only means the suggested-recipient lookup is still running - the guest check is past.
        => ShowStep(model.Step == ShareStep.None ? ShareStep.ContactSelection : model.Step);

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

    private void ShowStep(ShareStep step)
    {
        if (step == _displayedStep)
            return;

        switch (step) {
            case ShareStep.SignIn:
                SetStepView(new SignInView(Hub));
                break;
            case ShareStep.ContactSelection:
                SetStepView(new ContactSelectionView(Hub));
                break;
            case ShareStep.Uploading:
                SetStepView(new UploadProgressView(Hub));
                break;
            case ShareStep.Failed:
                SetStepView(new ErrorView(Hub));
                break;
            case ShareStep.Completed:
                SetStepView(new SuccessView(Hub));
                break;
        }
        _displayedStep = step;
    }

    private void SetStepView(StatefulView view)
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
