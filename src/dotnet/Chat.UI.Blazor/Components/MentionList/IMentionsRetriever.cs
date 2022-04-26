namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMentionsRetriever
{
    Task<IEnumerable<Mention>> GetMentions(string search, int limit, CancellationToken cancellationToken);
}
