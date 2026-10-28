using ActualLab.Fusion.Blazor;

namespace ActualChat.Contacts;

/// <summary>
/// What a chat list row needs about a chat beyond its news and read position, in one RPC call.
/// Each part changes rarely, so a new message never resends it.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record ContactSummary(
    [property: DataMember, Key(0)] Contact Contact,
    [property: DataMember, Key(1)] Mention? LastOwnMention,
    [property: DataMember, Key(2)] ChatUserSettings ChatUserSettings);
