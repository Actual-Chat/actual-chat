namespace ActualChat.MLSearch.ApiAdapters;

internal interface ILoggerSource
{
    ILogger GetLogger(Type ownerType);
}

internal class LoggerSource(IServiceProvider services) : ILoggerSource
{
    public ILogger GetLogger(Type ownerType) => services.LogFor(ownerType);
}

internal sealed class NullLoggerSource : ILoggerSource
{
    private NullLoggerSource() { }
    public static NullLoggerSource Instance => new();

    public ILogger GetLogger(Type ownerType) => NullLoggerFactory.Instance.CreateLogger(ownerType);
}
