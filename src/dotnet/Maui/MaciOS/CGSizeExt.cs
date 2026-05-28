namespace ActualChat.Maui;

public static class CGSizeExt
{
    extension(CGSize size)
    {
        public int LongSide => (int)Math.Max(size.Width, size.Height);
        public int ShortSide => (int)Math.Min(size.Width, size.Height);
    }
}
