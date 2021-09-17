namespace ActualChat.Generators
{
    public class StreamIdGenerator : ISlaveIdentifierGenerator<StreamId, AudioRecordId>
    {
        public StreamId Next(AudioRecordId master) 
            => throw new System.NotImplementedException();
    }
}