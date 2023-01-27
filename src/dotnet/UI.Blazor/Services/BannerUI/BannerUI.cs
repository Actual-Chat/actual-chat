using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components.Rendering;
using Stl.Extensibility;

namespace ActualChat.UI.Blazor.Services;

public class BannerUI
{
    private readonly object _lock = new ();
    private readonly IMutableState<ImmutableList<BannerInstance>> _bannerInstances;
    private IMatchingTypeFinder MatchingTypeFinder { get; }

    public IState<ImmutableList<BannerInstance>> BannerInstances => _bannerInstances;

    public BannerUI(IServiceProvider serviceProvider)
    {
        MatchingTypeFinder = serviceProvider.GetRequiredService<IMatchingTypeFinder>();
        _bannerInstances = serviceProvider.StateFactory().NewMutable(ImmutableList<BannerInstance>.Empty);
    }

    public BannerInstance Show<TBannerModel>(TBannerModel bannerModel)
        where TBannerModel : notnull
    {
        var componentType = MatchingTypeFinder.TryFind(bannerModel.GetType(), typeof(IBannerView<>))
            ?? throw StandardError.NotFound<TBannerModel>($"No banner view found for model {typeof(TBannerModel)}");
 #pragma warning disable IL2072
        var instance = CreateInstance(bannerModel, componentType);
 #pragma warning restore IL2072

        var bannerInstances = _bannerInstances;
        lock (_lock)
            bannerInstances.Value = bannerInstances.Value.Add(instance);

        return instance;
    }

    private BannerInstance CreateInstance<TBannerModel>(
        TBannerModel bannerModel,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
        where TBannerModel : notnull
    {
        var instance = new BannerInstance(Close);
#pragma warning disable MA0123
        var i = 0;
        instance.View = cascadingValueBuilder => {
            cascadingValueBuilder.OpenComponent<CascadingValue<BannerInstance>>(i++);
            cascadingValueBuilder.AddAttribute(i++, nameof(CascadingValue<BannerInstance>.Value), instance);
            cascadingValueBuilder.AddAttribute(i++, nameof(CascadingValue<BannerInstance>.IsFixed), true);
            cascadingValueBuilder.AddAttribute(i++,
                nameof(CascadingValue<BannerInstance>.ChildContent),
                (RenderFragment)CreateBannerView);
            cascadingValueBuilder.CloseComponent();
        };
        return instance;

        void CreateBannerView(RenderTreeBuilder bannerViewBuilder)
        {
            bannerViewBuilder.OpenComponent(i++, componentType);
            bannerViewBuilder.AddAttribute(i++, nameof(IBannerView<TBannerModel>.BannerModel), bannerModel);
            bannerViewBuilder.CloseComponent();
        }
#pragma warning restore MA0123
    }

    private void Close(BannerInstance bannerInstance)
    {
        var bannerInstances = _bannerInstances;
        lock (_lock)
            bannerInstances.Value = bannerInstances.Value.Remove(bannerInstance);
    }
}
