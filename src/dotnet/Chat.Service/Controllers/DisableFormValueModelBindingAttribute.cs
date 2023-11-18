using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Chat.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    { }
}
