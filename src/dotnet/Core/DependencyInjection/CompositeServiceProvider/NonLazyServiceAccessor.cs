using ActualLab.Internal;

namespace ActualChat.DependencyInjection;

public class NonLazyServiceAccessor : IServiceProvider
{
    private readonly Lock _lock = new();

    public IServiceProvider NonLazyServices {
        get => field ?? throw Errors.NotInitialized(nameof(NonLazyServices));
        set {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            lock (_lock) {
                if (field != null)
                    throw Errors.AlreadyInitialized(nameof(NonLazyServices));
                field = value;
            }
        }
    }

    public object? GetService(Type serviceType)
        => NonLazyServices.GetService(serviceType);
}
