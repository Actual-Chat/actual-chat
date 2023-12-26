using ActualChat.Feedback;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI(UIHub hub) : ScopedServiceBase<UIHub>(hub)
{
    private ModalRef? _modal;

    private ModalUI ModalUI => Hub.ModalUI;
    private UICommander UICommander => Hub.UICommander();

    public async Task AskFeatureRequestFeedback(string feature, string? featureTitle = null)
    {
        if (_modal != null)
            return;

        var model = new FeatureRequestModal.Model { FeatureTitle = featureTitle };
        _modal = await ModalUI.Show(model).ConfigureAwait(true);
        await _modal.WhenClosed.ConfigureAwait(false);
        var hasSubmitted = model.HasSubmitted;
        _modal = null;
        if (!hasSubmitted)
            return;

        var command = new Feedbacks_FeatureRequest(Session, feature) {
            Rating = model.Rating,
            Comment = model.Comment,
        };
        await UICommander.Run(command, CancellationToken.None).ConfigureAwait(false);
    }
}
