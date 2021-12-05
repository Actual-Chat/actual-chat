using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Web.Internal;

public class IdentifierModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var modelType = context.Metadata.ModelType;
        return modelType.IsAssignableFrom(typeof(IIdentifier<string>))
            ? new StringIdentifierModelBinder()
            : null;
    }
}
