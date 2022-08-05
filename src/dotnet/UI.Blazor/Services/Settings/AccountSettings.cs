using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class AccountSettings : KvasClient
{
    public AccountSettings(IServerKvas serverKvas, Session session) : base(serverKvas, session) { }
}
