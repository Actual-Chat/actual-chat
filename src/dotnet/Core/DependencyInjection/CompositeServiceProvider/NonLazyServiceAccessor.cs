using Stl.Internal;

namespace ActualChat.DependencyInjection;

public class NonLazyServiceAccessor : IServiceProvider
{
    private readonly object _lock = new();
    private IServiceProvider? _nonLazyServices;

    public IServiceProvider NonLazyServices {
        get => _nonLazyServices ?? throw Errors.NotInitialized(nameof(NonLazyServices));
        set {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            lock (_lock) {
                if (_nonLazyServices != null)
                    throw Errors.AlreadyInitialized(nameof(NonLazyServices));
                _nonLazyServices = value;
            }
        }
    }

    public object? GetService(Type serviceType)
        => NonLazyServices.GetService(serviceType);
}
