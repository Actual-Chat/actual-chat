using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Web.Internal;

public class SymbolIdentifierModelBinder : IModelBinder
{
    private Func<string?, ISymbolIdentifier>? _parser;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext == null)
            throw new ArgumentNullException(nameof(bindingContext));

        var modelName = bindingContext.ModelName;
        var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        try {
            if (_parser == null) {
                var pValue = Expression.Parameter(typeof(string), "value");
                var mParse = bindingContext.ModelType.GetMethod(
                    "Parse",
                    BindingFlags.Public | BindingFlags.Static,
                    new[] { typeof(string) });
                _parser = Expression
                    .Lambda<Func<string?, ISymbolIdentifier>>(Expression.Call(mParse!, pValue), pValue)
                    .Compile();
            }
            var value = valueProviderResult.FirstValue ?? "";
            var result = _parser.Invoke(value);
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        catch {
            bindingContext.Result = ModelBindingResult.Failed();
        }
        return Task.CompletedTask;
    }
}
