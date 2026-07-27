namespace ActualChat.Resilience;

/// <summary>
/// Maps an inbound RPC call to its <see cref="RateLimitClass"/> by the name of its command type.
/// A null result means the call isn't charged.
/// </summary>
public sealed record RateLimitClassResolver
{
    public static readonly RateLimitClassResolver Default = new();
    public string[] AuthCommandPrefixes { get; init; } = [
        "EmailAuth_",
        "PhoneAuth_",
        "Invites_Use",
        "Accounts_ConfirmRegister",
    ];

    public RateLimitClass? Resolve(string commandName, bool isCommand)
    {
        // Reads are compute calls served from cache, so their natural rate is thousands per second
        if (!isCommand)
            return null;

        foreach (var prefix in AuthCommandPrefixes)
            if (commandName.StartsWith(prefix))
                return RateLimitClass.Auth;

        return RateLimitClass.Command;
    }
}
