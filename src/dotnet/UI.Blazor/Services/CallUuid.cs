using System.Security.Cryptography;
using System.Text;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Maps a <see cref="CallId"/> to the stable UUID the platform call UI identifies the call
/// by, so a push, a cancel, and a restarted app all agree without a lookup.
/// </summary>
public static class CallUuid
{
    public static Guid For(CallId callId)
    {
        // Name-based, so it survives a process restart - the VoIP push that carries a call
        // id is routinely what starts the process. The first 16 bytes of SHA-256 over the
        // call id; the version bits don't matter to CallKit, only stability does.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(callId.Value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
