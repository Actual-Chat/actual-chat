namespace ActualChat.Hosting;

public class ClientInfoProvider
{
    public ClientInfo ClientInfo { get; private set; } = Hosting.ClientInfo.Default;

    public void SetClientInfo(ClientInfo clientInfo)
        => ClientInfo = clientInfo;
}
