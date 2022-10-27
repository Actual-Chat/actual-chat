using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Chat.Internal;

#pragma warning disable IL2075

public class ChatPrincipalIdModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        try {
            var sValue = valueProviderResult.FirstValue ?? "";
            var result = new ChatPrincipalId(sValue);
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        catch {
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
