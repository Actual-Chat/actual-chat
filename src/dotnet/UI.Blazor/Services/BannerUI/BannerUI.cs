using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Services;

public class BannerUI : ScopedServiceBase<UIHub>
{
    private readonly object _lock = new();
    private readonly MutableState<ImmutableList<BannerDef>> _banners;

    private TypeMapper<IBannerView> ViewResolver { get; }

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<ImmutableList<BannerDef>> Banners => _banners;

    public BannerUI(UIHub hub) : base(hub)
    {
        _banners = StateFactory.NewMutable(
            ImmutableList<BannerDef>.Empty,
            StateCategories.Get(GetType(), nameof(Banners)));
        ViewResolver = hub.GetRequiredService<TypeMapper<IBannerView>>();
    }

    public BannerDef Show<TBannerModel>(TBannerModel bannerModel)
        where TBannerModel : notnull
    {
        var componentType = ViewResolver.Get(bannerModel.GetType());
#pragma warning disable IL2072
        var banner = Create(bannerModel, componentType);
#pragma warning restore IL2072

        lock (_lock)
            _banners.Value = _banners.Value.Add(banner);
        return banner;
    }

    private BannerDef Create<TBannerModel>(
        TBannerModel bannerModel,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
        where TBannerModel : notnull
    {
        return new BannerDef(View, OnDismiss);

#pragma warning disable MA0123
        RenderFragment View(BannerDef instance)
        {
            return RenderBannerWrapper;

            void RenderBannerWrapper(RenderTreeBuilder builder)
            {
                var i = 0;
                builder.OpenComponent<CascadingValue<BannerDef>>(i++);
                builder.AddAttribute(i++, nameof(CascadingValue<BannerDef>.Value), instance);
                builder.AddAttribute(i++, nameof(CascadingValue<BannerDef>.IsFixed), true);
                builder.AddAttribute(i++,
                    nameof(CascadingValue<BannerDef>.ChildContent),
                    (RenderFragment)RenderBanner);
                builder.CloseComponent();
            }

            void RenderBanner(RenderTreeBuilder bannerViewBuilder)
            {
                var j = 0;
                bannerViewBuilder.OpenComponent(j++, componentType);
                bannerViewBuilder.AddAttribute(j++, nameof(IBannerView<TBannerModel>.BannerModel), bannerModel);
                bannerViewBuilder.CloseComponent();
            }
        }
#pragma warning restore MA0123
    }

    private void OnDismiss(BannerDef banner)
    {
        lock (_lock)
            _banners.Value = _banners.Value.Remove(banner);
    }
}
