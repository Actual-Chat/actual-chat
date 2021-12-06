namespace ActualChat;

public static class ArrayExt
{
    public static void Deconstruct<T>(this T[] array, out T? first, out T[] rest)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));

        if (array.Length > 0) {
            first = array[0];
            rest = array[1..];
        }
        else {
            first = default;
            rest = Array.Empty<T>();
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
