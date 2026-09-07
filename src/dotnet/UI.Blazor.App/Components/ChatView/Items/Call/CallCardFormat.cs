using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// The one place the outcome-by-reader wording table lives. Both render paths call it — the card
/// for a call that never connected, and the conversation item's call mode for one that did.
/// </summary>
public static class CallCardFormat
{
    public static (string Icon, string Title, string? Hint, bool IsCallBack) Get(
        CallOutcome outcome, bool isCaller, IStringLocalizer l)
        => outcome switch {
            CallOutcome.NoAnswer when isCaller =>
                ("icon-call-out", l.Call_Entry_Outgoing, l.Call_Entry_NoAnswer, false),
            CallOutcome.NoAnswer =>
                ("icon-phone-missed", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            CallOutcome.Declined =>
                ("icon-phone-off", l.Call_Entry_Declined, null, false),
            CallOutcome.Canceled when isCaller =>
                ("icon-phone-off", l.Call_Entry_Canceled, null, false),
            CallOutcome.Canceled =>
                ("icon-phone-missed", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            _ => ("icon-phone-call", l.Call_Entry_Ended, null, false),
        };
}
