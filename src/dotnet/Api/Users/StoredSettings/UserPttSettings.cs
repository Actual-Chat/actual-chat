using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for Push to Talk: which chats may wake the device,
/// and how the hands-free gestures behave.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserPttSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserPttSettings>
{
    // Matches ActiveChatsUI.MaxActiveChatCount, and bounds server wake fan-out per speaker.
    public const int MaxChatCount = 3;

    // Legacy mirror of PttChats (ids only), kept in sync for pre-epoch readers.
    [DataMember, Key(0)]
    public ChatId[] PttChatIds { get; init; } = [];
    [DataMember, Key(1)]
    public string Origin { get; init; } = "";
    [DataMember, Key(2)]
    public bool IsFlipToTalkEnabled { get; init; } = true;
    [DataMember, Key(3)]
    public bool IsDoubleShakeEnabled { get; init; } = true;
    [DataMember, Key(4)]
    public ShakeSensitivity ShakeSensitivity { get; init; } = ShakeSensitivity.Medium;
    [DataMember, Key(5)]
    public bool AreGesturesAlwaysOn { get; init; }
    // The getter caps: blobs written when the UI offered a 2-minute option must read as the cap.
    [DataMember, Key(6)]
    public TimeSpan HotWindow {
        get => field > Constants.Audio.PttHotWindowMax ? Constants.Audio.PttHotWindowMax : field;
        init;
    } = TimeSpan.FromSeconds(60);
    [DataMember, Key(7)]
    public bool AreAudibleCuesEnabled { get; init; } = true;
    // Nullable, read as `?? true`: a blob predating this member reads it as default, not as `= true`.
    [DataMember, Key(8)]
    public bool? IsHeadsetButtonEnabled { get; init; }
    // Nullable, read as `?? true`: a blob predating this member reads it as default, not as `= true`.
    [DataMember, Key(9)]
    public bool? IsPttTransmitEnabled { get; init; }
    // The getter normalizes: a blob predating this member deserializes it as null, not as `= []`.
    [DataMember, Key(10)]
    public PttChat[] PttChats { get => field ?? []; init; } = [];
    // How long after an incoming utterance ends (or the app opens) the reply triggers stay armed.
    // The getter normalizes: a blob predating this member deserializes it as zero, not as the default.
    [DataMember, Key(11)]
    public TimeSpan AnswerWindow {
        get => field == default ? Constants.Audio.PttAnswerWindowDefault : field;
        init;
    }
    // How long a hush gesture mutes every armed chat; zero from an old blob reads as the default.
    [DataMember, Key(12)]
    public TimeSpan HushDuration {
        get => field == default ? Constants.Audio.PttHushDurationDefault : field;
        init;
    }
    // Nullable, read as `?? true`: a blob predating this member reads it as default, not as `= true`.
    [DataMember, Key(13)]
    public bool? IsHushGestureEnabled { get; init; }

    // PttChats + legacy PttChatIds-only entries (surfaced with JoinedAt = default, so never armed).
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public PttChat[] AllPttChats
        => PttChats
            .Concat(PttChatIds
                .Except(PttChats.Select(c => c.ChatId))
                .Select(id => new PttChat(id, default)))
            .ToArray();

    public static bool IsArmed(Moment? pttEnabledAt, Moment joinedAt)
        => pttEnabledAt is { } enabledAt && joinedAt >= enabledAt;

    public bool IsArmedIn(ChatId chatId, Moment? pttEnabledAt)
        => PttChats.Any(c => c.ChatId == chatId && IsArmed(pttEnabledAt, c.JoinedAt));

    public bool IsMutedIn(ChatId chatId, Moment now)
        => PttChats.Any(c => c.ChatId == chatId && c.IsMutedAt(now));

    public UserPttSettings WithPttChat(ChatId chatId, Moment joinedAt)
    {
        var pttChats = AllPttChats
            .Where(c => c.ChatId != chatId)
            .Append(new PttChat(chatId, joinedAt))
            .ToArray();
        while (pttChats.Length > MaxChatCount)
            pttChats = pttChats.Without(pttChats.MinBy(c => c.JoinedAt)!).ToArray();
        return WithPttChats(pttChats);
    }

    public UserPttSettings WithoutPttChat(ChatId chatId)
        => WithPttChats(AllPttChats.Where(c => c.ChatId != chatId).ToArray());

    public UserPttSettings WithPttChatMuted(ChatId chatId, Moment mutedAt, Moment mutedUntil)
        => WithPttChats(AllPttChats
            .Select(c => c.ChatId == chatId ? c with { MutedAt = mutedAt, MutedUntil = mutedUntil } : c)
            .ToArray());

    public UserPttSettings WithPttChatUnmuted(ChatId chatId)
        => WithPttChats(AllPttChats
            .Select(c => c.ChatId == chatId ? c with { MutedAt = null, MutedUntil = null } : c)
            .ToArray());

    public UserPttSettings WithAllPttChatsMuted(Moment mutedAt, Moment mutedUntil)
        => WithPttChats(AllPttChats
            .Select(c => c.MutedUntil is { } until && until >= mutedUntil
                ? c
                : c with { MutedAt = mutedAt, MutedUntil = mutedUntil })
            .ToArray());

    public UserPttSettings WithPttChatMutesRestored(IReadOnlyCollection<PttChat> priorEntries)
        // Puts each chat's mute back the way it was before a hush touched it - unmuted, or muted
        // for the shorter period the user had chosen - rather than clearing it outright.
        => WithPttChats(AllPttChats
            .Select(c => priorEntries.FirstOrDefault(p => p.ChatId == c.ChatId) is { } prior
                ? c with { MutedAt = prior.MutedAt, MutedUntil = prior.MutedUntil }
                : c)
            .ToArray());

    public UserPttSettings WithPttChatsUnmuted(IReadOnlyCollection<ChatId> chatIds)
        => WithPttChats(AllPttChats
            .Select(c => chatIds.Contains(c.ChatId) ? c with { MutedAt = null, MutedUntil = null } : c)
            .ToArray());

    public UserPttSettings WithOnlyPttChats(IReadOnlySet<ChatId> chatIds)
        // Drops entries the caller no longer sees as armed, so a dead one can't consume the
        // MaxChatCount budget and make WithPttChat evict a live chat in its place.
        => WithPttChats(AllPttChats.Where(c => chatIds.Contains(c.ChatId)).ToArray());

    // Private methods

    private UserPttSettings WithPttChats(PttChat[] pttChats)
        => this with { PttChats = pttChats, PttChatIds = pttChats.Select(c => c.ChatId).ToArray() };
}

/// <summary>
/// A per-chat Push to Talk consent entry; armed only while <see cref="JoinedAt"/> is within
/// the chat's current enable-epoch (>= <c>Chat.PttEnabledAt</c>), and inert while muted
/// (<see cref="MutedUntil"/> is in the future) without losing the consent.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PttChat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Moment JoinedAt,
    [property: DataMember, Key(2)] Moment? MutedAt = null,
    [property: DataMember, Key(3)] Moment? MutedUntil = null)
{
    public bool IsMutedAt(Moment now)
        => MutedUntil is { } mutedUntil && now < mutedUntil;
}

// Values are ordered so Medium is the zero default; the firing sets nest: Low ⊆ Medium ⊆ High.
public enum ShakeSensitivity
{
    Medium = 0,
    Low = 1,
    High = 2,
}
