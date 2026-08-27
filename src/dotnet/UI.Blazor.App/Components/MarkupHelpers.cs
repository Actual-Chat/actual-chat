using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class MarkupHelpers(AppUIHub hub)
{
    private AppUIHub Hub { get; } = hub;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private DateFormatter DateFormatter => Hub.DateFormatter;
    private MomentClockSet Clocks => Hub.Clocks;

    public MarkupString LastEntryTime(Moment? moment)
    {
        var timestamp = "";
        if (moment.HasValue) {
            var now = DateTimeConverter.ToLocalTime(Clocks.SystemClock.Now);
            var beginsAt = DateTimeConverter.ToLocalTime(moment.Value);
            timestamp = DateFormatter.FormatListTime(beginsAt, now);
        }
        return new MarkupString($"<div class=\"last-entry-time\">{timestamp}</div>");
    }
}
