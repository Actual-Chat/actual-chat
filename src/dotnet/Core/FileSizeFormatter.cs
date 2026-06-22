namespace ActualChat;

public static class FileSizeFormatter
{
    public static string Format(long byteCount)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
        if (byteCount == 0)
            return "0" + suf[0];

        long bytes = Math.Abs(byteCount);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(byteCount) * num).Format() + suf[place];
    }

    public static string FormatProgress(long current, long total)
    {
        if (total <= 0)
            return Format(total);

        var place = Convert.ToInt32(Math.Floor(Math.Log(total, 1024)));
        var cur = Math.Max(current, 0) / Math.Pow(1024, place);
        return $"{cur.ToString("F2", CultureInfo.InvariantCulture)} / {Format(total)}";
    }
}
