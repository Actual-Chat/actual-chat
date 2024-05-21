using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public interface IDeveloperTools
{
    public bool IsEnabled(AccountFull account);
}
