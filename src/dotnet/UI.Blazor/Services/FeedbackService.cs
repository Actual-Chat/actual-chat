using ActualChat.Feedback;
using Blazored.Modal;
using Blazored.Modal.Services;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackService
{
    private readonly Session _session;
    private readonly IModalService _modalService;
    private readonly IFeedbacks _feedback;
    private IModalReference? _modalReference;

    public FeedbackService(Session session, IModalService modalService, IFeedbacks feedback)
    {
        _session = session;
        _modalService = modalService;
        _feedback = feedback;
    }

    public async Task AskFeatureRequestFeedback(string feature)
    {
        if (_modalReference != null)
            return;
        _modalReference = _modalService.Show<FeatureRequestFeedback>("", new ModalOptions { HideHeader = true});
        var result = await _modalReference.Result.ConfigureAwait(false);
        _modalReference = null;
        if (result.Cancelled)
            return;

        var data = ((int,string))result.Data;
        var command = new IFeedbacks.FeatureRequestCommand(_session, feature) {
            Rating = data.Item1,
            Comment = data.Item2
        };
        await _feedback.CreateFeatureRequest(command, default).ConfigureAwait(false);
    }
}
