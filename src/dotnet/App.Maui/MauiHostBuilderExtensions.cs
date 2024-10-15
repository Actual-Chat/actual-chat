using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Handlers;

namespace ActualChat.App.Maui;

public static class MauiHostBuilderExtensions
{
    public static MauiAppBuilder UseMauiBlazorApp<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(this MauiAppBuilder builder)
        where TApp : class, IApplication
    {
        builder.Services.TryAddSingleton<IApplication, TApp>();
        var dependencyServiceType = typeof(DependencyService);
        dependencyServiceType.GetMethod("SetToInitialized", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, []);
        var addDependencyTypeIfNeededMethodInfo = dependencyServiceType.GetMethod("AddDependencyTypeIfNeeded", BindingFlags.Static | BindingFlags.NonPublic);
        var resourceLoaderType = Type.GetType("Microsoft.Maui.Controls.Xaml.ResourcesLoader, Microsoft.Maui.Controls.Xaml");
        var valueConverterProviderType = Type.GetType("Microsoft.Maui.Controls.Xaml.ValueConverterProvider, Microsoft.Maui.Controls.Xaml");
        addDependencyTypeIfNeededMethodInfo!.Invoke(null, [resourceLoaderType]);
        addDependencyTypeIfNeededMethodInfo!.Invoke(null, [valueConverterProviderType]);
        Type? resourceProviderType = null;
#if WINDOWS
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.UWP.WindowsResourcesProvider, Microsoft.Maui.Controls")!;
#elif ANDROID
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.Android.ResourcesProvider, Microsoft.Maui.Controls")!;
#elif IOS
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.iOS.ResourcesProvider, Microsoft.Maui.Controls")!;
#endif
        addDependencyTypeIfNeededMethodInfo.Invoke(null, [resourceProviderType]);

        builder
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<Page, PageHandler>();
                handlers.AddHandler<Window, WindowHandler>();
                handlers.AddHandler<Application, ApplicationHandler>();
            });

        typeof(VisualElement).GetMethod("RemapForControls", BindingFlags.Static | BindingFlags.NonPublic, [])!.Invoke(null, []);
        typeof(ContentPage).GetMethod("RemapForControls", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, []);

        return builder;
    }
}
