using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public sealed class AccountSettings : ServerKvasClient
{
    public AccountSettings(IServerKvas serverKvas, Session session) : base(serverKvas, session) { }
}
