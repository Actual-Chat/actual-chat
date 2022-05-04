using ActualChat.Feedback;
using Blazored.Modal;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI
{
    private readonly Session _session;
    private readonly ModalUI _modalUI;
    private readonly IFeedbacks _feedbacks;
    private IModalReference? _modal;

    public FeedbackUI(Session session, ModalUI modalUI, IFeedbacks feedbacks)
    {
        _session = session;
        _modalUI = modalUI;
        _feedbacks = feedbacks;
    }

    public async Task AskFeatureRequestFeedback(string feature, string? featureTitle = null)
    {
        if (_modal != null)
            return;

        var model = new FeatureRequestModal.Model { FeatureTitle = featureTitle };
        _modal = _modalUI.Show(model);
        var result = await _modal.Result.ConfigureAwait(false);
        _modal = null;
        if (result.Cancelled)
            return;

        var command = new IFeedbacks.FeatureRequestCommand(_session, feature) {
            Rating = model.Rating,
            Comment = model.Comment
        };
        await _feedbacks.CreateFeatureRequest(command, default).ConfigureAwait(false);
    }
}
