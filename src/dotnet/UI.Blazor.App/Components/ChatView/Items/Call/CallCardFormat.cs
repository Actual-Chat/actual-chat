using ActualChat.Localization;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// The one place the outcome-by-reader wording table lives. Both render paths call it — the card
/// for a call that never connected, and the conversation item's call mode for one that did.
/// </summary>
public static class CallCardFormat
{
    public static bool IsCaller(Conversation conversation, ChatContext chatContext)
        => conversation.CallerId == chatContext.Chat.Rules.Author?.Id;

    // The direction stays on the card once a summary gives the call a title: the resummarization that
    // writes one lands minutes after the card appears, and a label that vanishes then reads as a bug.
    public static TranslatedText Title(string callTitle, TranslatedText summaryTitle, IStringLocalizer l)
        => summaryTitle.Text.IsNullOrEmpty()
            ? TranslatedText.From(callTitle)
            : summaryTitle with { Text = l.Call_Entry_Titled_Format(callTitle, summaryTitle.Text) };

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
            CallOutcome.Ended when isCaller =>
                ("icon-call-arrow-out", l.Call_Entry_Outgoing, null, false),
            CallOutcome.Ended =>
                ("icon-call-arrow-in", l.Call_Entry_Incoming, null, false),
            // Reached by an outcome this build doesn't know - a row written by a newer server.
            _ => ("icon-phone-call", l.Call_Entry_Ended, null, false),
        };
}
