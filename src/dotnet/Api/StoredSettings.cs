using ActualLab.Rpc;
using ChatInviteSettings = ActualChat.Invite.ChatInviteSettings;

namespace ActualChat;

/// <summary>
/// Common base for all stored settings types, enabling polymorphic (union) serialization.
/// </summary>
[RpcSerializable]
[GenerateShape] // Force PolyType source-gen for the union dispatcher (avoids the reflection-emit
                // bug with large unions — see PolyType ReflectionEmitMemberAccessor / one-byte branch).
[DataContract, MemoryPackable]
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
// PolyType-native union dispatch for Nerdbank.MessagePack — tags match the MemoryPack ones above.
// User settings
[DerivedTypeShape(typeof(UserAppSettings), Tag = 0)]
[DerivedTypeShape(typeof(UserEmailsSettings), Tag = 1)]
[DerivedTypeShape(typeof(UserLanguageSettings), Tag = 2)]
[DerivedTypeShape(typeof(UserListeningSettings), Tag = 3)]
[DerivedTypeShape(typeof(UserNavbarSettings), Tag = 4)]
[DerivedTypeShape(typeof(UserReactionSettings), Tag = 5)]
[DerivedTypeShape(typeof(UserAvatarSettings), Tag = 6)]
[DerivedTypeShape(typeof(UserTranscriptionEngineSettings), Tag = 7)]
[DerivedTypeShape(typeof(UserOnboardingSettings), Tag = 8)]
[DerivedTypeShape(typeof(UserBubbleSettings), Tag = 9)]
[DerivedTypeShape(typeof(UserChatRecordingDetectedLanguage), Tag = 10)]
[DerivedTypeShape(typeof(ChatListSettings), Tag = 11)]
[DerivedTypeShape(typeof(UserTranscodingTestSettings), Tag = 12)]
[DerivedTypeShape(typeof(FakeDeviceContactOptions), Tag = 13)]
[DerivedTypeShape(typeof(UserReplaySettings), Tag = 14)]
// Chat-User settings
[DerivedTypeShape(typeof(ChatUserSettings), Tag = 50)]
[DerivedTypeShape(typeof(ChatInviteSettings), Tag = 51)]
[DerivedTypeShape(typeof(AddChatMembersBannerUserSettings), Tag = 52)]
// Local settings
[DerivedTypeShape(typeof(LocalAppSettings), Tag = 100)]
[DerivedTypeShape(typeof(LocalOnboardingSettings), Tag = 101)]
public abstract partial record StoredSettings
{
    /// <summary>
    /// Validates the key used to store this settings type.
    /// Override in derived types that require key validation (e.g., ChatId-scoped settings).
    /// </summary>
    public virtual void ValidateKey(string key)
    { }
}
