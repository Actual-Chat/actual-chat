namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMemberSelectorDataProvider
{
    Task<ApiArray<UserId>> ListUserIds(CancellationToken cancellationToken);
    Task<ApiArray<UserId>> ListPreSelectedUserIds(CancellationToken cancellationToken);
}
