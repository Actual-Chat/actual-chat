namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatViewNavigation(
    long EntryLid,
    bool MustHighlight,
    bool ShowInTheMiddle = true,
    bool ShouldRestoreViewPosition = false,
    bool KeepConversationsCollapsed = false)
{
    // This record relies on referential equality
    public bool Equals(ChatViewNavigation? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
