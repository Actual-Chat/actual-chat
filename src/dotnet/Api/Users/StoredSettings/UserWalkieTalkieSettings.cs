using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for walkie-talkie push-to-talk: which chats may wake the
/// device, and how the hands-free gestures behave.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserWalkieTalkieSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserWalkieTalkieSettings>
{
    // Matches ActiveChatsUI.MaxActiveChatCount, and bounds server wake fan-out per speaker.
    public const int MaxChatCount = 3;

    [DataMember, MemoryPackOrder(0), Key(0)]
    public ChatId[] PttChatIds { get; init; } = [];
    [DataMember, MemoryPackOrder(1), Key(1)]
    public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(2), Key(2)]
    public bool IsFlipToTalkEnabled { get; init; } = true;
    [DataMember, MemoryPackOrder(3), Key(3)]
    public bool IsDoubleShakeEnabled { get; init; } = true;
    [DataMember, MemoryPackOrder(4), Key(4)]
    public ShakeSensitivity ShakeSensitivity { get; init; } = ShakeSensitivity.Medium;
    [DataMember, MemoryPackOrder(5), Key(5)]
    public bool AreGesturesAlwaysOn { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)]
    public TimeSpan HotWindow { get; init; } = TimeSpan.FromSeconds(60);
    [DataMember, MemoryPackOrder(7), Key(7)]
    public bool AreAudibleCuesEnabled { get; init; } = true;
    // Nullable, read as `?? true`: a blob predating this member reads it as default, not as `= true`.
    [DataMember, MemoryPackOrder(8), Key(8)]
    public bool? IsHeadsetButtonEnabled { get; init; }

    public UserWalkieTalkieSettings WithPttChat(ChatId chatId)
        => this with { PttChatIds = PttChatIds.WithOrSkip(chatId).ToArray() };

    public UserWalkieTalkieSettings WithoutPttChat(ChatId chatId)
        => this with { PttChatIds = PttChatIds.Without(chatId).ToArray() };
}

// Values are ordered so Medium is the zero default; the firing sets nest: Low ⊆ Medium ⊆ High.
public enum ShakeSensitivity
{
    Medium = 0,
    Low = 1,
    High = 2,
}
