namespace ActualChat.Collections;

public static class ArrayExt
{
    // TryGetValue

    public static bool TryGetValue<T>(this T[] array, T item, out T? foundItem)
    {
        var index = Array.IndexOf(array, item);
        if (index < 0) {
            foundItem = default;
            return false;
        }

        foundItem = array[index];
        return true;
    }

    // CommonPrefixLength

    public static int CommonPrefixLength<T>(this T[] first, T[] second, IEqualityComparer<T>? comparer = null)
    {
        var c = comparer ?? EqualityComparer<T>.Default;
        var length = Math.Min(first.Length, second.Length);
        for (var i = 0; i < length; i++) {
            if (!c.Equals(first[i], second[i]))
                return i;
        }
        return length;
    }

    public static int CommonPrefixLength<T>(this ReadOnlySpan<T> first, ReadOnlySpan<T> second, IEqualityComparer<T>? comparer = null)
    {
        var c = comparer ?? EqualityComparer<T>.Default;
        var length = Math.Min(first.Length, second.Length);
        for (var i = 0; i < length; i++) {
            if (!c.Equals(first[i], second[i]))
                return i;
        }
        return length;
    }

    // Deconstruct

    public static void Deconstruct<T>(this T[] array, out T? first, out T[] rest)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));

        if (array.Length > 0) {
            first = array[0];
            rest = array[1..];
        }
        else {
            first = default;
            rest = [];
        }
    }

    public static void Deconstruct<T>(this T[] array, out T? first, out T? second, out T[] rest)
        => (first, (second, rest)) = array;

    public static void Deconstruct<T>(this T[] array, out T? first, out T? second, out T? third, out T[] rest)
        => (first, second, (third, rest)) = array;

    public static void Deconstruct<T>(this T[] array, out T? first, out T? second, out T? third, out T? fourth, out T[] rest)
        => (first, second, third, (fourth, rest)) = array;

    public static void Deconstruct<T>(this T[] array, out T? first, out T? second, out T? third, out T? fourth, out T? fifth, out T[] rest)
        => (first, second, third, fourth, (fifth, rest)) = array;
}
