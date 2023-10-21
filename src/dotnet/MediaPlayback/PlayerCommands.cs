namespace ActualChat.MediaPlayback;

public interface IPlayerCommand { }

public sealed class PlayCommand : IPlayerCommand
{
    public static readonly PlayCommand Instance = new();

    private PlayCommand() { }
}

public sealed class PauseCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly PauseCommand Instance = new();

    private PauseCommand() { }
}

public sealed class ResumeCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly ResumeCommand Instance = new();

    private ResumeCommand() { }
}

public sealed class AbortCommand : IPlayerCommand, IPlaybackCommand
{
    public static readonly AbortCommand Instance = new();

    private AbortCommand() { }
}

public sealed class EndCommand : IPlayerCommand
{
    public static readonly EndCommand Instance = new();

    private EndCommand() { }
}
