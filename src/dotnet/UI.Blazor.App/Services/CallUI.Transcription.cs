using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public partial class CallUI
{
    // Picked while the own call still dials: no session holds it yet, so it waits for the answer
    private readonly MutableState<(ChatId ChatId, bool IsOn)?> _dialingTranscription;

    private LiveSessionUI LiveSessionUI => Hub.LiveSessionUI;

    // Public methods

    // Null when the dialing call in this chat has no choice of its own
    [ComputeMethod]
    public virtual async Task<bool?> GetDialingTranscription(ChatId chatId, CancellationToken cancellationToken)
    {
        var dialingTranscription = await _dialingTranscription.Use(cancellationToken).ConfigureAwait(false);
        return dialingTranscription is { } choice && choice.ChatId == chatId ? choice.IsOn : null;
    }

    public void SetDialingTranscription(ChatId chatId, bool isOn)
    {
        if (_activeCall.Value is { Phase: CallPhase.Dialing } call && call.ChatId == chatId)
            _dialingTranscription.Value = (chatId, isOn);
    }

    // Protected/internal methods

    [ComputeMethod]
    protected virtual async Task<bool?> MustApplyDialingTranscription(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // Null while the choice still waits: for the answer, then for the answered call's session.
        // False once the call is over without one.
        var call = await GetActiveCall(cancellationToken).ConfigureAwait(false);
        if (call is not { Phase: CallPhase.Dialing or CallPhase.Active } || call.ChatId != chatId)
            return false;
        if (call.Phase == CallPhase.Dialing)
            return null;

        var live = await LiveSessionUI.Get(chatId, cancellationToken).ConfigureAwait(false);
        return live is null ? null : true;
    }

    // Private methods

    private async Task ApplyDialingTranscription(CancellationToken cancellationToken)
    {
        while (true) {
            var cChoice = await _dialingTranscription.Computed
                .When(x => x is not null, cancellationToken)
                .ConfigureAwait(false);
            var chatId = cChoice.Value!.Value.ChatId;
            var cMustApply = await Computed
                .Capture(() => MustApplyDialingTranscription(chatId, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            cMustApply = await cMustApply.When(x => x is not null, cancellationToken).ConfigureAwait(false);
            // Read again: the choice may have flipped while the call was dialing
            if (cMustApply.Value == true && _dialingTranscription.Value is { } choice && choice.ChatId == chatId)
                await SetTranscription(chatId, choice.IsOn, cancellationToken).ConfigureAwait(false);
            _dialingTranscription.Value = null;
        }
    }

    private async Task SetTranscription(ChatId chatId, bool isOn, CancellationToken cancellationToken)
    {
        try {
            await LiveSessionUI.SetTranscription(chatId, isOn, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "Applying the transcription picked while dialing failed for chat #{ChatId}", chatId);
        }
    }
}
