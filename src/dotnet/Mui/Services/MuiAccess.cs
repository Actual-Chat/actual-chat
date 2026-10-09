namespace ActualChat.Mui;

public enum MuiAccess
{
    SignedOut,
    Denied,
    Granted,
}

public static class MuiAccessExt
{
    public static MuiAccess GetMuiAccess(this AccountFull? account)
        => account switch {
            null => MuiAccess.SignedOut,
            { IsGuest: true } => MuiAccess.SignedOut,
            { IsAdmin: true } => MuiAccess.Granted,
            _ => MuiAccess.Denied,
        };
}
