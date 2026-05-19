using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Regular chat message — text, audio transcript, or media post.
/// Carries no fields beyond <see cref="ChatEntry"/>; its concrete type is the discriminator.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record TextEntry : ChatEntry
{
    public TextEntry() : base((ChatEntryId)null!) { }

    [SerializationConstructor]
    public TextEntry(ChatEntryId id, long version = 0) : base(id, version) { }
}
