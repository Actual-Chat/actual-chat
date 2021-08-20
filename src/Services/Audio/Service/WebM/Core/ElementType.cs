namespace ActualChat.Audio.WebM
{
    /// <summary>
    /// Defines the EBML element types
    /// </summary>
    public enum ElementType
    {
        /// <summary>
        /// The signed integer
        /// </summary>
        SignedInteger,

        /// <summary>
        /// The unsigned integer
        /// </summary>
        UnsignedInteger,

        /// <summary>
        /// The floating point number
        /// </summary>
        Float,

        /// <summary>
        /// The character string in the ASCII encoding
        /// </summary>
        AsciiString,

        /// <summary>
        /// The character string in the UTF-8 encoding
        /// </summary>
        Utf8String,

        /// <summary>
        /// The date
        /// </summary>
        Date,

        /// <summary>
        /// The binary data
        /// </summary>
        Binary,

        /// <summary>
        /// Contains other EBML sub-elements of the next lower level
        /// </summary>
        MasterElement,

        None = -1
    }
}