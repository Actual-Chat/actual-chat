using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Abstract base for system-generated chat entries — events such as members joining,
/// leaving, or being notified. Concrete kinds: <see cref="MembersChangedEntry"/>,
/// <see cref="NotifyMembersEntry"/>.
/// </summary>
[RpcSerializable]
[DataContract, MessagePackObject]
[Union(0, typeof(MembersChangedEntry))]
[Union(1, typeof(NotifyMembersEntry))]
public abstract partial record SystemEntry(ChatEntryId Id, long Version = 0) : ChatEntry(Id, Version);
