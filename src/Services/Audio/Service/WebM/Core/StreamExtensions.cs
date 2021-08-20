using System.IO;

namespace ActualChat.Audio.WebM
{
    public static class StreamExtensions
    {
        public static int ReadFully(this Stream stream, byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            int totalBytesRead = 0;

            do
            {
                bytesRead = stream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
                totalBytesRead += bytesRead;
            } while (bytesRead > 0 && totalBytesRead < count);

            return totalBytesRead;
        }
    }
}
