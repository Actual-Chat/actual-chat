using ActualChat.Users;

namespace ActualChat.UI.App.Services.NativeAppSettings;

public interface IDeveloperTools
{
    public bool IsEnabled(AccountFull account);
}
