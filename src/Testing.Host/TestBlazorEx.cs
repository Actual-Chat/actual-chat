using ActualChat.Host;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Blazor;

namespace ActualChat.Testing
{
    public static class TestBlazorEx
    {
        public static BlazorTester NewBlazorTester(this AppHost appHost)
            => new(appHost);

        public static void AddAuthenticationState<TComponent>(
            this ComponentParameterCollectionBuilder<TComponent> parameters,
            BlazorTester blazorTester)
            where TComponent: IComponent
        {
            var authStateProvider = blazorTester.ScopedAppServices.GetRequiredService<AuthStateProvider>();
            parameters.AddCascadingValue(authStateProvider.GetAuthenticationStateAsync());
        }
    }
}
