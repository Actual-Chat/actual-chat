using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// User preferences for walkie-talkie push-to-talk: which chats may wake the
/// device, and how the hands-free gestures behave.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserWalkieTalkieSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserWalkieTalkieSettings>
{
    // Matches ActiveChatsUI.MaxActiveChatCount, and bounds server wake fan-out per speaker.
    public const int MaxChatCount = 3;

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
