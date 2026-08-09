using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.Components;

public class ErrorView(IosHub hub) : ComputedStateView<ErrorView.Model>(hub)
{
    private ShareUI ShareUI => Hub.ShareUI;

    protected override void OnInitialRender(Model model)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;

        var contentView = new ErrorContentView(model.Message, Safe(UIKitExt.CloseApp)) {
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        AddSubview(contentView);

        NSLayoutConstraint.ActivateConstraints([
            contentView.TopAnchor.ConstraintEqualTo(TopAnchor),
            contentView.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            contentView.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            contentView.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        ]);
    }

    protected override void OnStateChanged(Model model)
    {
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var message = await ShareUI.FailureMessage.Use(cancellationToken).ConfigureAwait(false);
        return new Model(message);
    }

    // Nested types
    public sealed record Model(string Message);
}
