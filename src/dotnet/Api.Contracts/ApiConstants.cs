namespace ActualChat;

public static class ApiConstants
{
    public static class Concurrency
    {
        public static readonly int Unlimited = 0;
        public static readonly int High = 0; // Seemingly a better option than e.g. 256
        public static readonly int Low = 128;
    }
}
