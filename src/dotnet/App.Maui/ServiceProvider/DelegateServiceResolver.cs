namespace ActualChat.App.Maui;

public class DelegateServiceResolver
{
    private readonly object _lock = new ();
    private Func<Type,object?>? _implementationFactory;

    public void SetResolver(Func<Type, object?> implementationFactory)
    {
        lock (_lock) {
            if (_implementationFactory != null)
                throw StandardError.Constraint("Resolve delegate is already defined");
            _implementationFactory = implementationFactory;
        }
    }

    public object? GetService(Type serviceType)
    {
        lock (_lock) {
            if (_implementationFactory == null)
                throw StandardError.Constraint("Resolve delegate is not defined");
            return _implementationFactory(serviceType);
        }
    }
}
