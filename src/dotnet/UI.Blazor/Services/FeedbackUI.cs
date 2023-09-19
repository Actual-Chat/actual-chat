using ActualChat.Feedback;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI(IServiceProvider services)
{
    private readonly Session _session = services.Session();
    private readonly ModalUI _modalUI = services.GetRequiredService<ModalUI>();
    private readonly UICommander _uiCommander = services.GetRequiredService<UICommander>();
    private ModalRef? _modal;

    public async Task AskFeatureRequestFeedback(string feature, string? featureTitle = null)
    {
        if (_modal != null)
            return;

        var model = new FeatureRequestModal.Model { FeatureTitle = featureTitle };
        _modal = await _modalUI.Show(model).ConfigureAwait(true);
        await _modal.WhenClosed.ConfigureAwait(false);
        var hasSubmitted = model.HasSubmitted;
        _modal = null;
        if (!hasSubmitted)
            return;

        var command = new Feedbacks_FeatureRequest(_session, feature) {
            Rating = model.Rating,
            Comment = model.Comment,
        };
        await _uiCommander.Run(command, CancellationToken.None).ConfigureAwait(false);
    }
}
