using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Handlers;

namespace ActualChat.App.Maui;

/// <summary>
/// Extension methods for configuring a MAUI Blazor hybrid application with custom handlers.
/// </summary>
public static class MauiHostBuilderExtensions
{
    public static MauiAppBuilder UseMauiBlazorApp<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(this MauiAppBuilder builder)
        where TApp : class, IApplication
    {
        builder.Services.TryAddSingleton<IApplication, TApp>();
        SetToInitialized(null);
        var resourceLoaderType = Type.GetType("Microsoft.Maui.Controls.Xaml.ResourcesLoader, Microsoft.Maui.Controls.Xaml");
        var valueConverterProviderType = Type.GetType("Microsoft.Maui.Controls.Xaml.ValueConverterProvider, Microsoft.Maui.Controls.Xaml");
        AddDependencyTypeIfNeeded(null, resourceLoaderType!);
        AddDependencyTypeIfNeeded(null, valueConverterProviderType!);
        Type? resourceProviderType = null;
#if WINDOWS
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.UWP.WindowsResourcesProvider, Microsoft.Maui.Controls")!;
#elif ANDROID
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.Android.ResourcesProvider, Microsoft.Maui.Controls")!;
#elif IOS
        resourceProviderType = Type.GetType("Microsoft.Maui.Controls.Compatibility.Platform.iOS.ResourcesProvider, Microsoft.Maui.Controls")!;
#endif
        AddDependencyTypeIfNeeded(null, resourceProviderType!);

        builder
            .ConfigureMauiHandlers(handlers =>
            {
                handlers.AddHandler<Page, PageHandler>();
                handlers.AddHandler<Window, WindowHandler>();
                handlers.AddHandler<Application, ApplicationHandler>();
            });

        VisualElementRemapForControls(null);
        ContentPageRemapForControls(null);

        return builder;
    }

    // Private methods

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "SetToInitialized")]
    private static extern void SetToInitialized(
        [UnsafeAccessorType("Microsoft.Maui.Controls.DependencyService, Microsoft.Maui.Controls")] object? _);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "AddDependencyTypeIfNeeded")]
    private static extern void AddDependencyTypeIfNeeded(
        [UnsafeAccessorType("Microsoft.Maui.Controls.DependencyService, Microsoft.Maui.Controls")] object? _,
        Type type);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RemapForControls")]
    private static extern void VisualElementRemapForControls(VisualElement? _);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "RemapForControls")]
    private static extern void ContentPageRemapForControls(ContentPage? _);
}
