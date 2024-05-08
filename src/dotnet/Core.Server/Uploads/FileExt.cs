namespace ActualChat.Uploads;

public static class FileExt
{
    public static string ShortenFileName(string fileName, int lengthLimit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lengthLimit, 20);

        if (fileName.IsNullOrEmpty() || fileName.Length <= lengthLimit)
            return fileName;

        var ext = Path.GetExtension(fileName);
        if (ext.Length > 0) {
            var extLengthLimit = lengthLimit > 30 ? 15 : 5;
            if (ext.Length > extLengthLimit)
                ext = ext.Substring(0, extLengthLimit);
        }
        var remained = Path.GetFileNameWithoutExtension(fileName);
        const string namePartsSeparator = "__";
        var separatorLength = namePartsSeparator.Length;
        var partLength = (lengthLimit - ext.Length - separatorLength) / 2;
        var part1 = remained.Substring(0, partLength);
        var part2Length = lengthLimit - part1.Length - separatorLength;
        var part2 = remained.Substring(remained.Length - part2Length, part2Length);
        var newFileName = part1 + namePartsSeparator + part2;
        return newFileName + ext;
    }
}
