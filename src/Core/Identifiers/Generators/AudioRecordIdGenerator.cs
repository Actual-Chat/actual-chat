using Stl.Generators;

namespace ActualChat.Generators
{
    public class AudioRecordIdGenerator : IIdentifierGenerator<AudioRecordId>
    {
        public AudioRecordId Next()
            => (AudioRecordId)RandomStringGenerator.Default.Next(16, RandomStringGenerator.Base32Alphabet);
    }
}