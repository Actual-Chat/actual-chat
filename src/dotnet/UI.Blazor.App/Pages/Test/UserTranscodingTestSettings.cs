using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Pages.Test;

public static class KvasExt
{
    public static KvasAccessor<UserTranscodingTestSettings> UserTranscodingTestSettings(this IKvas<Account> kvas)
        => kvas.For<UserTranscodingTestSettings>();
}
