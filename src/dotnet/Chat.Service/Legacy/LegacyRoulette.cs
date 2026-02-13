using ActualChat.Roulette;

namespace ActualChat.Chat;

/// <summary>
/// Legacy IRoulette implementation for backwards compatibility with old clients.
/// Chat Roulette feature has been removed. Returns false for EnableChatRouletteUI.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
#pragma warning disable CS0618 // Obsolete
public class LegacyRoulette : ILegacyRoulette
{
    public virtual Task<bool> EnableChatRouletteUI(
        Session session,
        CancellationToken cancellationToken)
        => Task.FromResult(false);
}
#pragma warning restore CS0618
