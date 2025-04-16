namespace ActualChat.UI.Blazor.App.Components;

public class ChatEntryGroup : ChatMessage, IVirtualListGroup<ChatMessage>
{
public ChatEntryGroup(IReadOnlyList<ChatMessage> items)
    : base(items[0].Id)
{
    Items = items;
    ReplacementKind = ChatMessageReplacementKind.Group;
}

public override bool IsGroup => true;
public IReadOnlyList<ChatMessage> Items { get; }

public override bool Equals(ChatMessage? other)
{
    if (ReferenceEquals(null, other))
        return false;
    if (ReferenceEquals(this, other))
        return true;

    if (other is not ExpandedConversationMessage otherConversationMessage)
        return false;

    return Key.Equals(otherConversationMessage.Key)
        && ReplacementKind == other.ReplacementKind
        && Date == other.Date
        && Flags == other.Flags
        && Items.Count == otherConversationMessage.Items.Count
        && Items.SequenceEqual(otherConversationMessage.Items);
}

public override int GetHashCode()
    => Key.GetHashCode();
}
