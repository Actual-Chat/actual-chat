using ActualLab.Rpc;
using ChatInviteSettings = ActualChat.Invite.ChatInviteSettings;

namespace ActualChat;

/// <summary>
/// Common base for all stored settings types, enabling polymorphic (union) serialization.
/// </summary>
[RpcSerializable]
[DataContract, MemoryPackable, MessagePackObject]
// User settings
[MemoryPackUnion(0, typeof(UserAppSettings))]
[MemoryPackUnion(1, typeof(UserEmailsSettings))]
[MemoryPackUnion(2, typeof(UserLanguageSettings))]
[MemoryPackUnion(3, typeof(UserListeningSettings))]
[MemoryPackUnion(4, typeof(UserNavbarSettings))]
[MemoryPackUnion(5, typeof(UserReactionSettings))]
[MemoryPackUnion(6, typeof(UserAvatarSettings))]
[MemoryPackUnion(7, typeof(UserTranscriptionEngineSettings))]
[MemoryPackUnion(8, typeof(UserOnboardingSettings))]
[MemoryPackUnion(9, typeof(UserBubbleSettings))]
[MemoryPackUnion(10, typeof(UserChatRecordingDetectedLanguage))]
[MemoryPackUnion(11, typeof(ChatListSettings))]
[MemoryPackUnion(12, typeof(UserTranscodingTestSettings))]
[MemoryPackUnion(13, typeof(FakeDeviceContactOptions))]
[MemoryPackUnion(14, typeof(UserReplaySettings))]
// Chat-User settings
[MemoryPackUnion(50, typeof(ChatUserSettings))]
[MemoryPackUnion(51, typeof(ChatInviteSettings))]
[MemoryPackUnion(52, typeof(AddChatMembersBannerUserSettings))]
// Local settings
[MemoryPackUnion(100, typeof(LocalAppSettings))]
[MemoryPackUnion(101, typeof(LocalOnboardingSettings))]
// User settings
[Union(0, typeof(UserAppSettings))]
[Union(1, typeof(UserEmailsSettings))]
[Union(2, typeof(UserLanguageSettings))]
[Union(3, typeof(UserListeningSettings))]
[Union(4, typeof(UserNavbarSettings))]
[Union(5, typeof(UserReactionSettings))]
[Union(6, typeof(UserAvatarSettings))]
[Union(7, typeof(UserTranscriptionEngineSettings))]
[Union(8, typeof(UserOnboardingSettings))]
[Union(9, typeof(UserBubbleSettings))]
[Union(10, typeof(UserChatRecordingDetectedLanguage))]
[Union(11, typeof(ChatListSettings))]
[Union(12, typeof(UserTranscodingTestSettings))]
[Union(13, typeof(FakeDeviceContactOptions))]
[Union(14, typeof(UserReplaySettings))]
[Union(15, typeof(RecentMentions))]
[Union(16, typeof(RecentGifs))]
[Union(17, typeof(UserPttSettings))]
[Union(18, typeof(UserNotificationsPanelSettings))]
// Chat-User settings
[Union(50, typeof(ChatUserSettings))]
[Union(51, typeof(ChatInviteSettings))]
[Union(52, typeof(AddChatMembersBannerUserSettings))]
[Union(53, typeof(PttAllowBannerUserSettings))]
// Local settings
[Union(100, typeof(LocalAppSettings))]
[Union(101, typeof(LocalOnboardingSettings))]
public abstract partial record StoredSettings
{
    /// <summary>
    /// Validates the key used to store this settings type.
    /// Override in derived types that require key validation (e.g., ChatId-scoped settings).
    /// </summary>
    public virtual void ValidateKey(string key)
    { }
}
