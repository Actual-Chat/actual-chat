using System.Security.Cryptography;

namespace ActualChat.MLSearch.Module;

public sealed class MLIntegrations
{
    public Dictionary<string, ECDsa> Pubkeys {get; set;}
}