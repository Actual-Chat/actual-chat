using ActualChat.Feedback;
using Blazored.Modal;

namespace ActualChat.UI.Blazor.Services;

public class FeedbackUI
{
    private readonly Session _session;
    private readonly ModalUI _modalUI;
    private readonly UICommandRunner _cmd;
    private IModalReference? _modal;

    public FeedbackUI(Session session, ModalUI modalUI, UICommandRunner cmd)
    {
        _session = session;
        _modalUI = modalUI;
        _cmd = cmd;
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
            Comment = model.Comment,
        };
        await _cmd.Run(command, CancellationToken.None).ConfigureAwait(false);
    }
}
