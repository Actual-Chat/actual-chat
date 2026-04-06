namespace ActualChat.Media;

public static class SizeExt
{
    extension(Size size)
    {
        public Size Scale(double factor)
            => new((int)(size.Width * factor), (int)(size.Height * factor));

        public Size Fit(Size maxSize)
        {
            if (size.Width <= maxSize.Width && size.Height <= maxSize.Height)
                return size;

            var scale = Math.Min((double)maxSize.Width / size.Width, (double)maxSize.Height / size.Height);
            return size.Scale(scale);
        }
    }
}
