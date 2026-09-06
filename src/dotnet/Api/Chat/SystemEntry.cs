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
public abstract partial record SystemEntry(ChatEntryId Id, long Version = 0)
    : ChatEntry(Id, Version), IForwardCompatibleUnion<SystemEntry>
{
    static SystemEntry? IForwardCompatibleUnion<SystemEntry>.NewUnsupported(
        int tag, ref MessagePackReader payload, MessagePackSerializerOptions options)
        // Under this root every tag is a system entry, so ChatEntry's classification doesn't apply.
        => (SystemEntry)NewUnsupported(true, ref payload, options);
}
