namespace ActualChat;

public static class ServerConstants
{
    public static class Backend
    {
        public static TimeSpan Timeout = TimeSpan.FromSeconds(1);
        public static TimeSpan LongTimeout = TimeSpan.FromSeconds(10);
        public static TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    }
}
