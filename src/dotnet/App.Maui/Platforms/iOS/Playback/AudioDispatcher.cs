namespace ActualChat.App.Maui.Playback;

public class AudioDispatcher
{
    private static readonly Lock Lock = new();

    public static void Invoke(Action action)
    {
        lock (Lock)
            action();
    }

    public static T Invoke<T>(Func<T> func)
    {
        lock (Lock)
            return func();
    }
}
