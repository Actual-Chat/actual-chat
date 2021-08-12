namespace ActualChat.Audio.Ebml.DO_NOT_USE
{
    /// <summary>
    /// EBML Variable Length Integer
    /// </summary>
    public readonly struct VInt
    {
        /// <summary>
        /// Maps length to data bits mask
        /// </summary>
        private static readonly ulong[] DataBitsMask =
        {
            (1L << 0) - 1,
            (1L << 7) - 1,
            (1L << 14) - 1,
            (1L << 21) - 1,
            (1L << 28) - 1,
            (1L << 35) - 1,
            (1L << 42) - 1,
            (1L << 49) - 1,
            (1L << 56) - 1
        };

        public readonly int Length;
        public readonly ulong EncodedValue;
        public readonly ulong Value;
        public readonly int Size;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActualChat.Audio.Ebml.VInt"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="length">The length.</param>
        /// <param name="encoded">The encoded.</param>
        public VInt(ulong value, int length, ulong encoded)
        {
            Value = value;
            Length = length;
            EncodedValue = encoded;
            Size = GetSize(Value);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActualChat.Audio.Ebml.VInt"/> struct.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="length">The length (optional).</param>
        public VInt(ulong value, int length = 0)
        {
            Value = value;
            (EncodedValue, Length) = Encode(value, length);
            Size = GetSize(value);
        }

        private static (ulong EncodedValue, int Length) Encode(ulong value, int length = 0)
        {
            if (length == 0)
            {
                while (DataBitsMask[++length] <= value) { }
            }

            var sizeMarker = 1UL << (7 * length);
            return (value | sizeMarker, length);
        }

        /// <summary>
        /// Returns the length of the VInt encoding for the specified value.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>The length</returns>
        private static int GetSize(ulong value)
        {
            int octets = 1;
            while ((value + 1) >> (octets * 7) != 0)
            {
                ++octets;
            }

            return octets;
        }

        public override string ToString()
        {
            return $"VInt, value = {Value}, length = {Length}, encoded = 0x{EncodedValue:X}";
        }
    }
}