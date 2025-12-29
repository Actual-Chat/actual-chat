namespace ActualChat.App.Maui.IosShareExt.UI.Fusion;

public static class FusionBuilderExt
{
    public static FusionIosBuilder AddIos(this FusionBuilder fusion)
        => new(fusion, null);

    public static FusionBuilder AddIos(this FusionBuilder fusion,
        Action<FusionIosBuilder>? configure)
        => new FusionIosBuilder(fusion, configure).Fusion;
}
