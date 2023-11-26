using System.Diagnostics.CodeAnalysis;

namespace ActualChat.App.Maui;
using JObject = Java.Lang.Object;

public static class JObjectExt
{
    public static bool IsNull([NotNullWhen(false)] this JObject? obj)
        => obj == null || obj.Handle == 0;
    public static bool IsNotNull([NotNullWhen(true)] this JObject? obj)
        => obj != null && obj.Handle != 0;

    public static T? IfNotNull<T>(this T? obj)
        where T : JObject
        => obj != null && obj.Handle != 0 ? obj : null;
}
