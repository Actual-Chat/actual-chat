using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Resources;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.Localization;

namespace ActualChat.UI.Blazor.App.Components;

public class MarkupHelpers(AppUIHub hub)
{
    private AppUIHub Hub { get; } = hub;
    private DateTimeConverter DateTimeConverter => Hub.DateTimeConverter;
    private DateFormatter DateFormatter => Hub.DateFormatter;
    private IStringLocalizer L => Hub.StringLocalizer;
    private MomentClockSet Clocks => Hub.Clocks;

    public MarkupString LastEntryTime(Moment? moment)
    {
        var timestamp = "";
        if (moment.HasValue) {
            var now = DateTimeConverter.ToLocalTime(Clocks.SystemClock.Now);
            var beginsAt = DateTimeConverter.ToLocalTime(moment.Value);
            var date = DateFormatter.FormatRelativeDate(beginsAt, now);
            var daysDelta = (now.Date - beginsAt.Date).Days;
            timestamp = daysDelta switch {
                0 => beginsAt.ToString("t", DateFormatter),
                < 8 and > 0 => L.Date_Parts_Format(date, beginsAt.ToString("t", DateFormatter)),
                _ => date,
            };
        }
        return new MarkupString($"<div class=\"last-entry-time\">{timestamp}</div>");
    }
}
