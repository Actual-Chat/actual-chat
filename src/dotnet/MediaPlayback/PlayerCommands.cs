namespace ActualChat.MediaPlayback;

public interface IPlayerCommand { }

public sealed class PlayCommand : IPlayerCommand
{
    public static PlayCommand Instance { get; } = new();
    private PlayCommand() { }
}

public sealed class PauseCommand : IPlayerCommand, IPlaybackCommand
{
 public static PauseCommand Instance { get; } = new();
 private PauseCommand() { }
}

public sealed class ResumeCommand : IPlayerCommand, IPlaybackCommand
{
    public static ResumeCommand Instance { get; } = new();
    private ResumeCommand() { }
}

public sealed class AbortCommand : IPlayerCommand, IPlaybackCommand
{
    public static AbortCommand Instance { get; } = new();
    private AbortCommand() { }
}

public sealed class EndCommand : IPlayerCommand
{
    public static EndCommand Instance { get; } = new();
    private EndCommand() { }
}
