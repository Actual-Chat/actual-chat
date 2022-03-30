namespace ActualChat.MediaPlayback;

public interface IPlayerCommand { }

public sealed class PlayCommand : IPlayerCommand
{
    public static readonly PlayCommand Instance = new();
    private PlayCommand() { }
}

public sealed class StopCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly StopCommand Instance = new();
    private StopCommand() { }
}

public sealed class EndCommand : IPlayerCommand
{
    public static readonly EndCommand Instance = new();
    private EndCommand() { }
}
