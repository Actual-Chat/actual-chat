namespace ActualChat.App.Maui;
using JObject = Java.Lang.Object;

public static class JavaObjectExt
{
    public static bool IsValid([NotNullWhen(true)] this JObject? obj)
        => obj is not null && obj.Handle != IntPtr.Zero;

    public static T? IfValid<T>(this T? obj)
        where T : JObject
        => obj is not null && obj.Handle != IntPtr.Zero
            ? obj
            : null;

    public static HoldScope<T> Hold<T>(this T? obj)
        where T : JObject
        => new(obj);

    // Nested types

    public struct HoldScope<T>(T? target) : IDisposable
        where T : JObject
    {
        public T? Target = target.IfValid();
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public bool IsValid => Target is not null;

        public void Dispose()
            => Target = null;
    }
}
