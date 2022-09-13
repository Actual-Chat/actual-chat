using Blazored.Modal.Services;

namespace Blazored.Modal;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlazoredModal(this IServiceCollection services)
        => services.AddScoped<IModalService, ModalService>();
}
