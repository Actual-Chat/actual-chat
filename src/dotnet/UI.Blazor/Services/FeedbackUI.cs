using ActualChat.Feedback;
using ActualChat.UI.Blazor.Components;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI
{
    private readonly Session _session;
    private readonly ModalUI _modalUI;
    private readonly UICommander _uiCommander;
    private ModalRef? _modal;

    public FeedbackUI(IServiceProvider services)
    {
        _session = services.GetRequiredService<Session>();
        _modalUI = services.GetRequiredService<ModalUI>();
        _uiCommander = services.GetRequiredService<UICommander>();
    }

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
