using ActualChat.Feedback;
using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI
{
    private readonly Session _session;
    private readonly IModalService _modalService;
    private readonly IFeedbacks _feedbacks;
    private IModalReference? _modalReference;

    public FeedbackUI(Session session, IModalService modalService, IFeedbacks feedbacks)
    {
        _session = session;
        _modalService = modalService;
        _feedbacks = feedbacks;
    }

    public async Task AskFeatureRequestFeedback(string feature, string? featureTitle = null)
    {
        if (_modalReference != null)
            return;

        var parameters = new ModalParameters();
        parameters.Add(nameof(FeatureRequestFeedback.FeatureTitle), featureTitle);
        var modalOptions = new ModalOptions { HideHeader = true, DisableBackgroundCancel = true, Class = "feature-modal"};
        _modalReference = _modalService.Show<FeatureRequestFeedback>("", parameters, modalOptions);
        var result = await _modalReference.Result.ConfigureAwait(false);
        _modalReference = null;
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
