namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMemberSelector : IMemberSelectorDataProvider
{
    Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken);
}
