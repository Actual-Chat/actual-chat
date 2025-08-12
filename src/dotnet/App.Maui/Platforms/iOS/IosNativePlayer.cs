using ActualChat.UI.Blazor;
using ActualLab.Concurrency;
using AVFoundation;

namespace ActualChat.App.Maui;

public class IosNativePlayer : IDisposable
{
    private static readonly AVAudioFormat Format = new (AVAudioCommonFormat.PCMFloat32, 48000, 1, true);
    private readonly AVAudioEngine _engine = new ();
    private readonly ConcurrentPool<PlayerNode> _playerNodePool;
    private UIHub Hub { get; }

    [field: AllowNull, MaybeNull]
    protected ILogger Log => field ??= Hub.LogFor(GetType());

    public IosNativePlayer(UIHub hub)
    {
        Hub = hub;
        _playerNodePool = new ConcurrentPool<PlayerNode>(() => PlayerNode.Create(_engine, Format));
    }

    public async Task Play(string soundName)
    {
        if (soundName.IsNullOrEmpty())
            return;

        try {
            using var playerNodeLease = _playerNodePool.Rent();
            var playerNode = playerNodeLease.Resource;
            await playerNode.PlayResourceFile(soundName).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to play sound {SoundName}", soundName);
        }
    }

    public void Dispose()
        // TODO: dispose pool with all nodes
        => _engine.Stop();
}
