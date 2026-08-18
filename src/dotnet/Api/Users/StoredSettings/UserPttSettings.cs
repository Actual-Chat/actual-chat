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
    [DataMember, Key(6)]
    public TimeSpan HotWindow { get; init; } = TimeSpan.FromSeconds(60);
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

    // Private methods

    private UserPttSettings WithPttChats(PttChat[] pttChats)
        => this with { PttChats = pttChats, PttChatIds = pttChats.Select(c => c.ChatId).ToArray() };
}

/// <summary>
/// A per-chat Push to Talk consent entry; armed only while <see cref="JoinedAt"/> is within
/// the chat's current enable-epoch (>= <c>Chat.PttEnabledAt</c>).
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PttChat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] Moment JoinedAt);

// Values are ordered so Medium is the zero default; the firing sets nest: Low ⊆ Medium ⊆ High.
public enum ShakeSensitivity
{
    Medium = 0,
    Low = 1,
    High = 2,
}
