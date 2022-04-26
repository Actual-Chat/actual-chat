namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMentionsRetriever
{
    Task<IEnumerable<MentionListItem>> GetMentions(string search, int limit, CancellationToken cancellationToken);
}
