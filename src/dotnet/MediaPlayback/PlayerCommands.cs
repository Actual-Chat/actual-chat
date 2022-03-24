using ActualChat.Media;

namespace ActualChat.MediaPlayback;

public interface IPlayerCommand { }

public sealed class PlayCommand : IPlayerCommand
{
    public static readonly PlayCommand Instance = new();
    private PlayCommand() { }
}

/// <summary> Playing is cancelled by user action or an exception. </summary>
public sealed class StopCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly StopCommand Instance = new();
    private StopCommand() { }
    /// <summary> You can't cancel stop command. </summary>
    public CancellationToken CancellationToken => default;
}

/// <summary> Occurs when EOF is reached. </summary>
public sealed class EndCommand : IPlayerCommand
{
    public static readonly EndCommand Instance = new();
    private EndCommand() { }
}
