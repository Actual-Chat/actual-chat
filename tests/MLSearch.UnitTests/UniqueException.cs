namespace ActualChat.MLSearch.UnitTests;

public class UniqueException : Exception
{
    public UniqueException() { }
    public UniqueException(string message) : base(message) { }
    public UniqueException(string message, Exception innerException) : base(message, innerException) { }
}
