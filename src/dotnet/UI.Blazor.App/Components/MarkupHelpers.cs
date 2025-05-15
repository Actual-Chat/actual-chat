using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class MarkupHelpers(ChatUIHub hub)
{
    private ChatUIHub Hub { get; } = hub;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private MomentClockSet Clocks => Hub.Clocks();

    public MarkupString LastEntryTime(Moment? moment)
    {
        var isToday = false;
        var isYesterday = false;
        var inWeek = false;

        var timestamp = "";
        if (moment.HasValue) {
            var now = DateTimeConverter.ToLocalTime(Clocks.SystemClock.Now);
            var beginsAt = DateTimeConverter.ToLocalTime(moment.Value);
            var delta = (now.Date - beginsAt.Date).Days;

            switch (delta) {
            case 0:
                isToday = true;
                break;
            case 1:
                isYesterday = true;
                break;
            case < 8:
                inWeek = true;
                break;
            }

            if (isToday)
                timestamp = beginsAt.ToShortTimeString();
            else if (isYesterday)
                timestamp = $"Yesterday, {beginsAt.ToShortTimeString()}";
            else if (inWeek)
                timestamp = beginsAt.ToInvariantString("ddd, HH:mm");
            else if (now.Year == beginsAt.Year)
                timestamp = beginsAt.ToInvariantString("MMM dd");
            else
                timestamp = beginsAt.ToInvariantString("MMM dd, yyyy");
        }
        return new MarkupString($"<div class=\"last-entry-time\">{timestamp}</div>");
    }
}
