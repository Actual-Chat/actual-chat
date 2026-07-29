using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Forwarding metadata for a <see cref="ChatEntry"/>. Present when the entry
/// was forwarded from another chat; null otherwise.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatEntryForwarded : ISanitized
{
    /// <summary>
    /// The original entry id. Can be <c>default</c> for peer-chat forwards where the link is suppressed.
    /// </summary>
    [DataMember, MemoryPackOrder(0), Key(0)] public ChatEntryId? ChatEntryId { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember, MemoryPackOrder(2), Key(2)] public Moment BeginsAt { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public string ChatTitle {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public string AuthorName {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryForwarded() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryForwarded? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
