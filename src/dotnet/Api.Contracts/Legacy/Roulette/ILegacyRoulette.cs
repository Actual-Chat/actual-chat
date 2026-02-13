namespace ActualChat.Roulette;

/// <summary>
/// Legacy IRoulette interface for backwards compatibility with old clients.
/// Chat Roulette feature has been removed.
/// Only EnableChatRouletteUI is needed as it's called by Features_EnableChatRouletteUI
/// regardless of whether the feature gate is enabled.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
public interface ILegacyRoulette : IComputeService
{
    [ComputeMethod]
    Task<bool> EnableChatRouletteUI(Session session, CancellationToken cancellationToken);
}
