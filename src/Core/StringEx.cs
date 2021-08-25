namespace ActualChat
{
    public static class StringEx
    {
        public static string? NullIfEmpty(this string? source)
            => string.IsNullOrEmpty(source) ? null : source;

        public static string? NullIfWhiteSpace(this string? source)
            => string.IsNullOrWhiteSpace(source) ? null : source;
    }
}
