namespace ActualChat.UI.Blazor.App.Components;

public interface IMemberSelector : IMemberSelectorDataProvider
{
    Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken);
}
