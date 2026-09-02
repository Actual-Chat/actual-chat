using System.Security.Cryptography;
using System.Text;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Maps a call's <see cref="ConversationId"/> to the stable UUID the platform call UI
/// identifies it by, so a push, a cancel, and a restarted app all agree without a lookup.
/// </summary>
public static class CallId
{
    public static Guid For(ConversationId conversationId)
    {
        // Name-based, so it survives a process restart - the VoIP push that carries a call
        // id is routinely what starts the process. The first 16 bytes of SHA-256 over the
        // conversation id; the version bits don't matter to CallKit, only stability does.
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(conversationId.Value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
