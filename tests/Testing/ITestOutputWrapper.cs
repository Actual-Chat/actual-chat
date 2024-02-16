namespace ActualChat.Testing;

public interface ITestOutputWrapper : ITestOutputHelper
{
    ITestOutputHelper Wrapped { get; }
}
