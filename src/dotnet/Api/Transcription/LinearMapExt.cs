namespace ActualChat.Transcription;

public static class LinearMapExt
{
    public static LinearMap Scale(this LinearMap timeMap, int fullLength, int newFullLength)
    {
        if (fullLength == newFullLength)
            return timeMap;

        var factor = newFullLength / (float)fullLength;
        return timeMap.Multiply(factor);
    }
}
