namespace ActualChat.DependencyInjection;

public class LazyService<T>: Lazy<T>
    where T : class
{
    public LazyService(IServiceProvider services)
        : base(services.GetRequiredService<T>)
    { }
}
