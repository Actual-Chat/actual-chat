namespace ActualChat.DI;

public class LazyInstance<T>: Lazy<T> where T : notnull
{
    public LazyInstance(IServiceProvider serviceProvider)
        : base(serviceProvider.GetRequiredService<T>)
    { }
}
