namespace ActualChat.Rpc;

public abstract class HostNameRemapper
{
    public static HostNameRemapper? Instance { get; set; }

    public abstract string Get(string hostName);
}
