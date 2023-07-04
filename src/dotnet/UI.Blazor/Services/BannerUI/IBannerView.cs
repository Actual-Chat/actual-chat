namespace ActualChat.UI.Blazor.Services;

public interface IBannerView
{ }

public interface IBannerView<TBannerModel> : IBannerView
{
    public TBannerModel BannerModel { get; set; }
}
