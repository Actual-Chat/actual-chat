using ActualChat.Feedback;
using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI
{
    private readonly Session _session;
    private readonly IModalService _modalService;
    private readonly IFeedbacks _feedbacks;
    private IModalReference? _modal;

    public FeedbackUI(Session session, IModalService modalService, IFeedbacks feedbacks)
    {
        _session = session;
        _modalService = modalService;
        _feedbacks = feedbacks;
    }

    public async Task AskFeatureRequestFeedback(string feature, string? featureTitle = null)
    {
        if (_modal != null)
            return;

        var parameters = new ModalParameters();
        parameters.Add(nameof(FeatureRequestModal.FeatureTitle), featureTitle);
        var modalOptions = new ModalOptions {
            HideHeader = true,
            DisableBackgroundCancel = false,
            Class = "modal",
        };
        _modal = _modalService.Show<FeatureRequestModal>("", parameters, modalOptions);
        var result = await _modal.Result.ConfigureAwait(false);
        _modal = null;
        if (result.Cancelled)
            return;

        var data = ((int,string))result.Data;
        var command = new IFeedbacks.FeatureRequestCommand(_session, feature) {
            Rating = data.Item1,
            Comment = data.Item2
        };
        await _feedbacks.CreateFeatureRequest(command, default).ConfigureAwait(false);
    }
}
