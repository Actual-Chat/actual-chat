using System.Text;

namespace ActualChat;

public static class StringBuilderExt
{
    [ThreadStatic]
    private static char[]? _threadStaticBuffer;

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

    // GetSuffix

    public static string GetSuffix(this StringBuilder source, int length, bool splitOnNewLine = true)
    {
        // Produces a tail of StringBuilder of a desirable length,
        // which may start after a newline character.
        var totalLength = source.Length;
        if (totalLength <= length)
            return source.ToString();

        var startIndex = totalLength - length;
        var chunkStart = 0;
        var buffer = _threadStaticBuffer;
        if (buffer is null || buffer.Length < length)
            buffer = _threadStaticBuffer = new char[length];
        var bufferPos = 0;
        foreach (var chunk in source.GetChunks()) {
            var span = chunk.Span;
            if (chunkStart + span.Length <= startIndex) {
                chunkStart += chunk.Length;
                continue; // Skip chunks entirely before startIndex
            }

            // chunkStart <= startIndex here
            var chunkSlice = span[Math.Max(0, startIndex - chunkStart)..];
            chunkSlice.CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += chunkSlice.Length;
            chunkStart += chunk.Length;
        }

        // The buffer is shared, so it may contain some extra characters,
        // and we need to take this into account.
        if (splitOnNewLine) {
            var bufferSpan = buffer[..bufferPos];
            var newlineIndex = bufferSpan.IndexOf('\n');
            if (newlineIndex >= 0)
                return new string(bufferSpan[(newlineIndex + 1)..]);
        }
        return new string(buffer, 0, bufferPos);
    }
}
