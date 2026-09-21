using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("UsageDays")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUsageDay
{
    public string UserId { get; set; } = "";

    public DateTime Day {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [ConcurrencyCheck] public long Version { get; set; }
    public long SpeechMs { get; set; }
    public int SpeechEntries { get; set; }
    public int Messages { get; set; }
    public int LiveSessions { get; set; }
    public int ContactsAdded { get; set; }
    public int ContactsRemoved { get; set; }

    public UsageDay ToModel()
        => new(Day.ToMoment(), SpeechMs, SpeechEntries, Messages, LiveSessions, ContactsAdded, ContactsRemoved);

    public void Apply(UsageEvent usageEvent)
    {
        switch (usageEvent.Kind) {
        case UsageEventKind.Speech:
            SpeechMs += usageEvent.Value;
            SpeechEntries++;
            break;
        case UsageEventKind.Message:
            Messages += (int)usageEvent.Value;
            break;
        case UsageEventKind.LiveSession:
            LiveSessions++;
            break;
        case UsageEventKind.Contact:
            if (usageEvent.Value >= 0)
                ContactsAdded += (int)usageEvent.Value;
            else
                ContactsRemoved -= (int)usageEvent.Value;
            break;
        }
    }
}
