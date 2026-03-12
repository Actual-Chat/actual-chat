namespace ActualChat;

public static partial class StandardError
{
    public static class AudioPlayer
    {
        public static Exception PlayingStateExpected(Type type)
            => StateTransition(type, "Play command should be called first.");
    }
}
