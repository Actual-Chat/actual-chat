using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Chat.Internal;

public class ChatPrincipalIdModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var modelType = context.Metadata.ModelType;
        return modelType.IsAssignableFrom(typeof(ChatPrincipalId))
            ? new ChatPrincipalIdModelBinder()
            : null;
    }
}
