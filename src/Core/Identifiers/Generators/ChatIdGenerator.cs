using Stl.Generators;

namespace ActualChat.Generators
{
    public class ChatIdGenerator : IIdentifierGenerator<ChatId>
    {
        public ChatId Next() 
            => (ChatId)RandomStringGenerator.Default.Next(8, RandomStringGenerator.Base32Alphabet);
    }
}