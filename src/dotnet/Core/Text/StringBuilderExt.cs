using System.Text;

namespace ActualChat;

public static class StringBuilderExt
{
    public static StringBuilder AppendJoin(
        this StringBuilder stringBuilder,
        IEnumerable<string> values,
        string separator = ", ")
        => stringBuilder.AppendJoin(values, (sb, value) => sb.Append(value), separator);

    public static StringBuilder AppendJoin(
        this StringBuilder stringBuilder,
        string separator,
        params string[] values)
        => stringBuilder.AppendJoin(values, (sb, value) => sb.Append(value), separator);

    public static StringBuilder AppendJoin<T>(
        this StringBuilder stringBuilder,
        IEnumerable<T> values,
        Action<StringBuilder, T> joinAction,
        string separator = ", ")
    {
        var appended = false;

        foreach (var value in values) {
            joinAction(stringBuilder, value);
            stringBuilder.Append(separator);
            appended = true;
        }

        if (appended)
            stringBuilder.Length -= separator.Length;

        return stringBuilder;
    }

    public static StringBuilder AppendJoin<T>(
        this StringBuilder stringBuilder,
        IEnumerable<T> values,
        Func<StringBuilder, T, bool> joinFunc,
        string separator = ", ")
    {
        var appended = false;

        foreach (var value in values)
            if (joinFunc(stringBuilder, value)) {
                stringBuilder.Append(separator);
                appended = true;
            }

        if (appended)
            stringBuilder.Length -= separator.Length;

        return stringBuilder;
    }

    public static StringBuilder AppendJoin<T, TParam>(
        this StringBuilder stringBuilder,
        IEnumerable<T> values,
        TParam param,
        Action<StringBuilder, T, TParam> joinAction,
        string separator = ", ")
    {
        var appended = false;

        foreach (var value in values) {
            joinAction(stringBuilder, value, param);
            stringBuilder.Append(separator);
            appended = true;
        }

        if (appended)
            stringBuilder.Length -= separator.Length;

        return stringBuilder;
    }
}
