using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualLab.Fusion.UI;

namespace ActualChat.App.Maui.IosShareExt.UI.Fusion;

public readonly struct FusionIosBuilder
{
    private sealed class AddedTag;
    private static readonly ServiceDescriptor AddedTagDescriptor = new(typeof(AddedTag), new AddedTag());

    public FusionBuilder Fusion { get; }
    public IServiceCollection Services => Fusion.Services;

    internal FusionIosBuilder(
        FusionBuilder fusion,
        Action<FusionIosBuilder>? configure)
    {
        Fusion = fusion;
        var services = Services;
        if (services.Contains(AddedTagDescriptor)) {
            configure?.Invoke(this);
            return;
        }

        // We want the above Contains call to run in O(1), so...
        services.Insert(0, AddedTagDescriptor);
        services.AddScoped(c => new UICommander(c));
        services.AddScoped(_ => new UIActionFailureTracker.Options());
        services.AddScoped(c => new UIActionFailureTracker(
            c.GetRequiredService<UIActionFailureTracker.Options>(), c));
        services.AddScoped(c => new IosHub(c));

        configure?.Invoke(this);
    }
}
