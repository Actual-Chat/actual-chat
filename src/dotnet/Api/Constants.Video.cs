namespace ActualChat;

public static partial class Constants
{
    public static class Video
    {
        public static readonly TimeSpan CancellationDelay = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan StreamExpirationDelay = TimeSpan.FromSeconds(30);
        public static readonly int RetentionBufferSize = 150; // ~5s at 30fps
        public static readonly int ConsumerBufferSize = 300; // ~10s at 30fps before slow consumer disconnect
    }
}
