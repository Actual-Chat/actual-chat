using System;

namespace ActualChat.Mathematics
{
    public static class DoubleArrayExt
    {
        public static int IndexOfLowerOrEqual(this double[] values, double value)
        {
            var result = -1;
            var minIndex = 0;
            var maxIndex1 = values.Length - 1;
            while (minIndex <= maxIndex1) {
                var index = minIndex + ((maxIndex1 - minIndex) >> 1);
                var diff = values[index] - value;
                if (diff <= 0) {
                    result = index;
                    minIndex = index + 1;
                }
                else
                    maxIndex1 = index - 1;
            }
            return result;
        }

        public static int IndexOfGreaterOrEqual(this double[] values, double value)
        {
            var result = -1;
            var minIndex = 0;
            var maxIndex1 = values.Length - 1;
            while (minIndex <= maxIndex1) {
                var index = minIndex + ((maxIndex1 - minIndex) >> 1);
                var diff = values[index] - value;
                if (diff < 0)
                    minIndex = index + 1;
                else {
                    result = index;
                    maxIndex1 = index - 1;
                }
            }
            return result;
        }

        public static bool IsStrictlyIncreasingSequence(this double[] values)
        {
            if (values.Length == 0)
                return false;
            var lastValue = values[0];
            for (var i = 1; i < values.Length; i++) {
                var value = values[i];
                if (value <= lastValue)
                    return false;
                lastValue = value;
            }
            return true;
        }
    }
}
