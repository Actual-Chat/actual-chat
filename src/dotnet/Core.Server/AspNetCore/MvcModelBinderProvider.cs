using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.AspNetCore;

public class MvcModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var tModel = context.Metadata.ModelType;
        return tModel switch {
            not null when tModel.IsAssignableTo(typeof(ISymbolIdentifier)) => new SymbolIdentifierModelBinder(),
            // NOTE(AY): Resolve other binders here
            _ => null,
        };
    }
}
