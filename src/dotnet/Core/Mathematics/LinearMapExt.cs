namespace ActualChat.Mathematics;

public static class LinearMapExt
{
    public static LinearMap GetDiffSuffix(this LinearMap left, LinearMap right)
    {
        if (left.Length > right.Length)
            (left, right) = (right, left);

        var diffStartsAt = 0;
        for (int i = left.Length - 1; i >= 0; i--) {
            var lPair = left.Points[i];
            var rPair = right.Points[i];
            if (lPair.Equals(rPair)) {
                diffStartsAt = i;
                break;
            }
        }

        return new LinearMap(right.Points[diffStartsAt..]);
    }
}
