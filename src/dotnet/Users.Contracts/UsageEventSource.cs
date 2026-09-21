using ActualChat.Contacts;

namespace ActualChat.Users;

/// <summary>
/// Turns the other services' change events into <see cref="UsageEvent"/>s; every source id is built here.
/// </summary>
public static class UsageEventSource
{
    // A voice entry longer than this is a broken row, not a talk
    public static readonly TimeSpan MaxSpeechDuration = TimeSpan.FromHours(4);
    public static readonly TimeSpan DefaultMinLiveSessionParticipation = TimeSpan.FromMinutes(1);

    public static UsageEvent? FromEntryChange(ChatEntry entry, ChatEntry? oldEntry, ChangeKind changeKind)
    {
        if (entry is not TextEntry || entry.IsSystemEntry)
            return null;

        var attributes = new UsageEventAttributes { ChatKind = entry.ChatId.Kind, IsViaApi = entry.IsViaApi };
        if (!entry.HasAudio) {
            return changeKind == ChangeKind.Create
                ? new UsageEvent(UsageEventKind.Message, entry.BeginsAt, entry.Id.Value, 1, attributes)
                : null;
        }

        var isEndSet = changeKind switch {
            ChangeKind.Create => entry.EndsAt is not null,
            ChangeKind.Update => oldEntry?.EndsAt is null && entry.EndsAt is not null,
            _ => false,
        };
        if (!isEndSet)
            return null;

        var duration = entry.EndsAt!.Value - entry.BeginsAt;
        if (duration <= TimeSpan.Zero)
            return null;

        if (duration > MaxSpeechDuration)
            duration = MaxSpeechDuration;
        return new UsageEvent(
            UsageEventKind.Speech, entry.BeginsAt, entry.Id.Value, (long)duration.TotalMilliseconds, attributes);
    }

    public static UsageEvent? FromLiveSessionEnd(
        LiveSessionEndedEvent ended, LiveSessionEndedMember member, TimeSpan? minParticipation = null)
    {
        var participation = ended.EndedAt - (member.JoinedAt > ended.StartedAt ? member.JoinedAt : ended.StartedAt);
        if (participation < (minParticipation ?? DefaultMinLiveSessionParticipation))
            return null;

        var attributes = new UsageEventAttributes {
            ChatKind = ended.ChatId.Kind,
            SessionKind = ended.Kind,
            ParticipantCount = ended.Members.Count,
        };
        var sourceId = $"{ended.ChatId}:{ended.StartedAt.EpochOffsetTicks}";
        return new UsageEvent(
            UsageEventKind.LiveSession, ended.EndedAt, sourceId, (long)participation.TotalMilliseconds, attributes);
    }

    public static UsageEvent? FromContactChange(Contact contact, Contact? oldContact, ChangeKind changeKind, Moment now)
    {
        var isFriend = changeKind != ChangeKind.Remove && IsFriend(contact);
        var wasFriend = oldContact is not null && IsFriend(oldContact);
        if (isFriend == wasFriend)
            return null;

        var sourceId = $"{contact.Id}:{contact.Version}";
        return new UsageEvent(UsageEventKind.Contact, now, sourceId, isFriend ? 1 : -1);
    }

    public static bool IsFriend(Contact contact)
        => contact.Kind == ContactKind.User && contact.UserId is not null && contact.State == ContactState.Regular;

    public static UsageEvent ActiveDay(Moment at)
    {
        var day = UsageDay.DayOf(at);
        return new UsageEvent(UsageEventKind.ActiveDay, day, day.ToDateTime().ToString("yyyy-MM-dd", null), 1);
    }
}
