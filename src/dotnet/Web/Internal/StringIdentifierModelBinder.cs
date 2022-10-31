using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Web.Internal;

#pragma warning disable IL2075

public class StringIdentifierModelBinder : IModelBinder
{
    private Func<string, IIdentifier>? _identifierFactory;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        try {
            if (_identifierFactory == null) {
                var stringArg = Expression.Parameter(typeof(string), "str");
                var ctor = bindingContext.ModelType.GetConstructor(new[] { typeof(string) });
                _identifierFactory = Expression
                    .Lambda<Func<string,IIdentifier>>(Expression.New(ctor!, stringArg), stringArg)
                    .Compile();
            }
            var sValue = valueProviderResult.FirstValue ?? "";
            var result = _identifierFactory(sValue);
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        catch {
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
