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
    // First unread message body (+ a short next message rolled in) — kept raw so the summary text
    // can be recomposed without re-reading entries.
    [DataMember(Order = 13), Key(13)]
    public string LeadText { get; init; } = "";
    [DataMember(Order = 14), Key(14)]
    public int BeepCount { get; init; }
    [DataMember(Order = 15), Key(15)]
    public Moment LastBeepAt { get; init; }
    // Messages included in LeadText (roll-in makes it 2), so the "+N more" tail never counts a
    // message the lead already shows. Old blobs deserialize this as 0 == 1.
    [DataMember(Order = 17), Key(17)]
    public int LeadCount { get; init; }

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
        string leadText;
        int leadCount;
        if (incomingStart > 0 && incomingStart < existingStart) {
            leadText = Text; // this message is now the earliest unread -> it becomes the lead
            leadCount = 1;
        }
        else if (e.LeadText.IsNullOrEmpty()) {
            // A legacy existing without a lead falls back to its own text (its latest message).
            leadText = e.Text.IsNullOrEmpty() ? Text : e.Text;
            leadCount = 1;
        }
        else {
            leadText = e.LeadText;
            leadCount = Math.Max(1, e.LeadCount);
            var isLeadShort = leadText.Length < Constants.Notification.MaxRecentMessageTextLength;
            var canRollIn = isLeadShort && !Text.IsNullOrEmpty();
            if (existingUnread == 1 && canRollIn) {
                leadText = $"{leadText}\n{Text}";
                leadCount++;
            }
        }

        // A gap between messages long enough to count as a conversation lull resets the beep
        // back-off, so this fresh message alerts immediately instead of inheriting the back-off.
        var isLull = SentAt - e.SentAt >= Constants.Notification.BeepResetPeriod;
        return this with {
            Version = e.Version,
            CreatedAt = e.CreatedAt,
            HandledAt = null,
            // An out-of-order earlier message must not regress the newest-activity timestamp.
            SentAt = Moment.Max(e.SentAt, SentAt),
            EntryLid = entryLid,
            StartEntryLid = startEntryLid,
            UnreadCount = existingUnread + 1,
            AuthorIds = authorIds,
            LeadText = leadText,
            LeadCount = leadCount,
            BeepCount = isLull ? 0 : e.BeepCount,
            LastBeepAt = isLull ? default : e.LastBeepAt,
        };
    }

    private static long MinPositive(long a, long b)
        => a <= 0 ? b : b <= 0 ? a : Math.Min(a, b);
}
