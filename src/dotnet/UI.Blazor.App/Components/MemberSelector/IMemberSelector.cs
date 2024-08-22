namespace ActualChat.UI.Blazor.App.Components;

public interface IMemberSelector : IMemberListSource
{
    Task<Exception?> Invite(UserId[] userIds, CancellationToken cancellationToken);
}
