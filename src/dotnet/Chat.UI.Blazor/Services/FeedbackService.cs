using ActualChat.Feedback;
using Blazored.Modal.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class FeedbackService
{
    private readonly Session _session;
    private readonly IModalService _modalService;
    private readonly IFeedback _feedback;

    public FeedbackService(Session session, IModalService modalService, IFeedback feedback)
    {
        _session = session;
        _modalService = modalService;
        _feedback = feedback;
    }

    public async Task AskFeatureRequestFeedback(string feature)
    {
        var modalRef = _modalService.Show<FeatureRequestFeedback>();
        var result = await modalRef.Result.ConfigureAwait(false);
        if (result.Cancelled)
            return;

        var data = ((int,string))result.Data;
        var command = new IFeedback.FeatureRequestCommand(_session, feature) {
            Rating = data.Item1,
            Comment = data.Item2
        };
        await _feedback.CreateFeatureRequest(command, default).ConfigureAwait(false);
    }
}
