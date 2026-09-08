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
        // Three glyphs, drawn from the design: the arrow carries the direction (there is no separate
        // direction mark in the card), and the cross is reserved for a call its own caller dropped.
        => outcome switch {
            CallOutcome.NoAnswer when isCaller =>
                ("icon-call-arrow-out", l.Call_Entry_Outgoing, l.Call_Entry_NoAnswer, false),
            CallOutcome.NoAnswer =>
                ("icon-call-arrow-in", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            CallOutcome.Declined when isCaller =>
                ("icon-call-arrow-out", l.Call_Entry_Declined, null, false),
            CallOutcome.Declined =>
                ("icon-call-arrow-in", l.Call_Entry_Declined, null, false),
            CallOutcome.Canceled when isCaller =>
                ("icon-call-cross", l.Call_Entry_Canceled, null, false),
            CallOutcome.Canceled =>
                ("icon-call-arrow-in", l.Call_Entry_Missed, l.Call_Entry_TapToCallBack, true),
            // No arrow on a finished call: its card is built from the conversation, which carries no
            // caller, so a direction here would be a guess - and both readers see the same card.
            _ => ("icon-phone-call", l.Call_Entry_Ended, null, false),
        };
}
