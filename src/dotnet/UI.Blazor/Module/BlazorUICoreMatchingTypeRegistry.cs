using ActualChat.Diff.Handlers;
using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Module;

public class BlazorUICoreMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(FeatureRequestModal.Model), typeof(IModalView).ToSymbol()), typeof(FeatureRequestModal)},
            {(typeof(ImageViewerModal.Model), typeof(IModalView).ToSymbol()), typeof(ImageViewerModal)},
            {(typeof(DemandUserInteractionModal.Model), typeof(IModalView).ToSymbol()), typeof(DemandUserInteractionModal)},
        };
}
