using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public interface IDeveloperTools
{
    public bool IsEnabled(AccountFull account);
}
