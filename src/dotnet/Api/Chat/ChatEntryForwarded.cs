using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Forwarding metadata for a <see cref="ChatEntry"/>. Present when the entry
/// was forwarded from another chat; null otherwise.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ChatEntryForwarded : ISanitized
{
    /// <summary>
    /// The original entry id. Can be <c>default</c> for peer-chat forwards where the link is suppressed.
    /// </summary>
    [DataMember, Key(0)] public ChatEntryId? ChatEntryId { get; init; }
    [DataMember, Key(1)] public AuthorId AuthorId { get; init; } = null!;
    [DataMember, Key(2)] public Moment BeginsAt { get; init; }
    [DataMember, Key(3)] public string ChatTitle {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";
    [DataMember, Key(4)] public string AuthorName {
        get => Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(field); init;
    } = "";

    [SerializationConstructor]
    public ChatEntryForwarded() { }

    // This record relies on referential equality
    public bool Equals(ChatEntryForwarded? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
