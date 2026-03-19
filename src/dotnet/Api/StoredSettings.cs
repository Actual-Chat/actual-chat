using ActualLab.Rpc;

namespace ActualChat;

/// <summary>
/// Common base for all stored settings types, enabling polymorphic (union) serialization.
/// </summary>
[RpcSerializable]
[DataContract, MemoryPackable, MessagePackObject(true)]
[MemoryPackUnion(0, typeof(UserAppSettings))]
[MemoryPackUnion(1, typeof(UserEmailsSettings))]
[MemoryPackUnion(2, typeof(UserLanguageSettings))]
[MemoryPackUnion(3, typeof(UserChatSettings))]
[MemoryPackUnion(4, typeof(UserListeningSettings))]
[MemoryPackUnion(5, typeof(UserNavbarSettings))]
[MemoryPackUnion(6, typeof(UserReactionSettings))]
[MemoryPackUnion(7, typeof(UserAvatarSettings))]
[MemoryPackUnion(8, typeof(UserTranscriptionEngineSettings))]
[MemoryPackUnion(9, typeof(UserOnboardingSettings))]
[MemoryPackUnion(10, typeof(UserBubbleSettings))]
[MemoryPackUnion(11, typeof(UserChatRecordingDetectedLanguage))]
[MemoryPackUnion(12, typeof(LocalAppSettings))]
[MemoryPackUnion(13, typeof(LocalOnboardingSettings))]
[MemoryPackUnion(14, typeof(ChatListSettings))]
[MemoryPackUnion(15, typeof(UserTranscodingTestSettings))]
[MemoryPackUnion(16, typeof(FakeDeviceContactOptions))]
[Union(0, typeof(UserAppSettings))]
[Union(1, typeof(UserEmailsSettings))]
[Union(2, typeof(UserLanguageSettings))]
[Union(3, typeof(UserChatSettings))]
[Union(4, typeof(UserListeningSettings))]
[Union(5, typeof(UserNavbarSettings))]
[Union(6, typeof(UserReactionSettings))]
[Union(7, typeof(UserAvatarSettings))]
[Union(8, typeof(UserTranscriptionEngineSettings))]
[Union(9, typeof(UserOnboardingSettings))]
[Union(10, typeof(UserBubbleSettings))]
[Union(11, typeof(UserChatRecordingDetectedLanguage))]
[Union(12, typeof(LocalAppSettings))]
[Union(13, typeof(LocalOnboardingSettings))]
[Union(14, typeof(ChatListSettings))]
[Union(15, typeof(UserTranscodingTestSettings))]
[Union(16, typeof(FakeDeviceContactOptions))]
public abstract partial record StoredSettings;
