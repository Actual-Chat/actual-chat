namespace ActualChat.Collections;

public static class BitArrayExt
{
    public static int SetBitCount(this BitArray bitArray)
    {
        var count = 0;
        for (var i = 0; i < bitArray.Length; i++) {
            if (bitArray[i])
                count++;
        }
        return count;
    }
}
