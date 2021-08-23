using System;
using System.IO;

namespace ActualChat.Audio.WebM
{
    public class EbmlDataFormatException : IOException
    {
        public EbmlDataFormatException()
        {
        }

        public EbmlDataFormatException(string message)
            : base(message)
        {
        }

        public EbmlDataFormatException(string message, Exception cause)
            : base(message, cause)
        {
        }
    }
}