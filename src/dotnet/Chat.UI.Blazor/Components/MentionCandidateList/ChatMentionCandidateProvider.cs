namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMentionCandidateProvider
{
    Task<MentionCandidate[]> GetMentionCandidates(string search, int limit, CancellationToken cancellationToken);
}

public class ChatMentionCandidateProvider : IMentionCandidateProvider
{
    private readonly Session _session;
    private readonly string _chatId;
    private readonly IChats _chats;

    public ChatMentionCandidateProvider(Session session, string chatId, IChats chats)
    {
        _session = session;
        _chatId = chatId;
        _chats = chats;
    }

    public async Task<MentionCandidate[]> GetMentionCandidates(
        string search, int limit,
        CancellationToken cancellationToken)
    {
        var authors = await _chats.ListMentionCandidates(_session, _chatId, cancellationToken).ConfigureAwait(false);
        var candidates = authors
            .Where(c => c.Name.OrdinalIgnoreCaseContains(search))
            .OrderBy(c => c.Name)
            .Take(limit);
        var filteredCandidates = candidates.Select(c => new MentionCandidate(c.Id, c.Name)).ToArray();
        return filteredCandidates;
    }
}
