using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Forwarding metadata for a <see cref="ChatEntry"/>. Present when the entry
/// was forwarded from another chat; null otherwise.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatEntryForwarded : ISanitized
{
    /// <summary>
    /// The original entry id. Can be <c>default</c> for peer-chat forwards where the link is suppressed.
    /// </summary>
    [DataMember, MemoryPackOrder(0)] public ChatEntryId ChatEntryId { get; init; }

    [DataMember, MemoryPackOrder(1)] public AuthorId AuthorId { get; init; }
    [DataMember, MemoryPackOrder(2)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public string ChatTitle { get => Sanitizer.MaskPrivate(field); init; } = "";
    [DataMember, MemoryPackOrder(4)] public string AuthorName { get => Sanitizer.MaskPrivate(field); init; } = "";

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryForwarded() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryForwarded? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
