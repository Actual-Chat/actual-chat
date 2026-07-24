namespace ActualChat.Notifications;

/// <summary>
/// Base for chat notifications that reference an entry but collapse per chat: the similarity
/// key is the <see cref="ChatId"/> and <see cref="EntryLid"/> is stored. Compare with
/// <see cref="ChatEntryNotification"/>, whose similarity key is the entry itself.
/// </summary>
[DataContract]
public abstract partial record ChatEntryRelatedNotification(NotificationId Id, long Version = 0)
    : ChatNotification(Id, Version)
{
    [DataMember(Order = 9), Key(9)]
    public long EntryLid { get; init; }
    // First unread entry the coalesced notification anchors at — the tap target, stable across
    // coalescing. Old blobs deserialize this as 0; the computed StartEntryId falls back to EntryLid.
    [DataMember(Order = 10), Key(10)]
    public long StartEntryLid { get; init; }
    [DataMember(Order = 11), Key(11)]
    public int UnreadCount { get; init; }
    [DataMember(Order = 12), Key(12)]
    public ApiArray<AuthorId> AuthorIds { get; init; }
    // Rolling-deploy compat only: old nodes/clients compose from LeadText (= newest message text)
    // and LeadCount (= 1). New code reads RecentMessages. Remove both in the next release.
    [DataMember(Order = 13), Key(13)]
    public string LeadText { get; init; } = "";
    [DataMember(Order = 14), Key(14)]
    public int BeepCount { get; init; }
    [DataMember(Order = 15), Key(15)]
    public Moment LastBeepAt { get; init; }
    [DataMember(Order = 17), Key(17)]
    public int LeadCount { get; init; }
    // The transcript window: last MaxRecentMessages unread messages, oldest -> newest.
    [DataMember(Order = 18), Key(18)]
    public ApiArray<NotificationMessage> RecentMessages { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId EntryId => ChatEntryId.New(ChatId, EntryLid);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId StartEntryId => ChatEntryId.New(ChatId, StartEntryLid > 0 ? StartEntryLid : EntryLid);

    public override Notification MergeWith(Notification? existing)
    {
        if (existing is not ChatEntryRelatedNotification e)
            return base.MergeWith(existing);

        // Notification events can be processed out of order, so anchor at the min (earliest) unread
        // entry and track the max (latest) — don't assume the existing one arrived first.
        var existingStart = e.StartEntryLid > 0 ? e.StartEntryLid : e.EntryLid;
        var incomingStart = StartEntryLid > 0 ? StartEntryLid : EntryLid;
        // An entry already inside the merged window is a redelivery (the queue is at-least-once),
        // so the merge must be idempotent: return the existing instance unchanged — the caller
        // relies on reference equality to skip the beep/push for no-op merges.
        if (EntryLid > 0 && EntryLid <= e.EntryLid && incomingStart >= existingStart)
            return e;

        var authorIds = e.AuthorIds;
        if (AuthorId is { } authorId && !authorIds.Contains(authorId) && authorIds.Count < Constants.Notification.MaxTrackedAuthors)
            authorIds = authorIds.With(authorId);
        var startEntryLid = MinPositive(existingStart, incomingStart);
        var entryLid = Math.Max(e.EntryLid, EntryLid);
        // Pre-coalescing blobs deserialize UnreadCount as 0 though they represent one unread entry.
        var existingUnread = Math.Max(1, e.UnreadCount);
        var recentMessages = MergeRecentMessages(e, this);
        var newestIsIncoming = EntryLid >= e.EntryLid;

        // A gap between messages long enough to count as a conversation lull resets the beep
        // back-off, so this fresh message alerts immediately instead of inheriting the back-off.
        var isLull = SentAt - e.SentAt >= Constants.Notification.BeepResetPeriod;
        return this with {
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            HandledAt = null,
            // An out-of-order earlier message must not regress the newest-activity timestamp,
            // and the banner headline (title/icon) must keep tracking the newest message.
            SentAt = Moment.Max(e.SentAt, SentAt),
            Title = newestIsIncoming ? Title : e.Title,
            IconUrl = newestIsIncoming ? IconUrl : e.IconUrl,
            EntryLid = entryLid,
            StartEntryLid = startEntryLid,
            UnreadCount = existingUnread + 1,
            AuthorIds = authorIds,
            RecentMessages = recentMessages,
            LeadText = recentMessages.IsEmpty ? "" : recentMessages[^1].Text,
            LeadCount = 1,
            BeepCount = isLull ? 0 : e.BeepCount,
            LastBeepAt = isLull ? default : e.LastBeepAt,
        };
    }

    // Moves the first-unread anchor to newStart: drops now-read messages from the transcript
    // window and approximates the remaining unread count from the entry span. The caller re-seeds
    // RecentMessages (from the entry store) when the window empties, then recomposes Text.
    public ChatEntryRelatedNotification ReAnchorAt(long newStart)
    {
        var recentMessages = RecentMessages.Where(m => m.EntryLid >= newStart).ToApiArray();
        return this with {
            StartEntryLid = newStart,
            UnreadCount = (int)Math.Max(1, EntryLid - newStart + 1),
            RecentMessages = recentMessages,
            LeadText = recentMessages.IsEmpty ? "" : recentMessages[^1].Text,
            LeadCount = 1,
        };
    }

    private static long MinPositive(long a, long b)
        => a <= 0 ? b : b <= 0 ? a : Math.Min(a, b);

    private static ApiArray<NotificationMessage> MergeRecentMessages(
        ChatEntryRelatedNotification existing, ChatEntryRelatedNotification incoming)
    {
        var messages = new List<NotificationMessage>(existing.RecentMessages.Count + 1);
        messages.AddRange(GetRecentMessages(existing));
        foreach (var message in GetRecentMessages(incoming))
            if (messages.All(m => m.EntryLid != message.EntryLid))
                messages.Add(message);
        messages.Sort((a, b) => a.EntryLid.CompareTo(b.EntryLid));
        if (messages.Count > Constants.Notification.MaxRecentMessages)
            messages.RemoveRange(0, messages.Count - Constants.Notification.MaxRecentMessages);
        return messages.ToApiArray();
    }

    // Pre-RecentMessages blobs carry their text in LeadText/Text; synthesize a message so the
    // merge upgrades them in place (empty author name -> the line renders without a prefix).
    private static IEnumerable<NotificationMessage> GetRecentMessages(ChatEntryRelatedNotification n)
    {
        if (!n.RecentMessages.IsEmpty)
            return n.RecentMessages;

        var text = n.LeadText.IsNullOrEmpty() ? n.Text : n.LeadText;
        return text.IsNullOrEmpty()
            ? []
            : [NotificationMessage.New(n.AuthorId ?? default, "", text, n.EntryLid, n.SentAt)];
    }
}
