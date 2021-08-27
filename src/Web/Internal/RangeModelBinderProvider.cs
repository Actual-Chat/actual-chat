using System;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ActualChat.Web.Internal
{
    public class RangeModelBinderProvider  : IModelBinderProvider
    {
        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var modelType = context.Metadata.ModelType;
            if (modelType.IsConstructedGenericType && modelType.GetGenericTypeDefinition() == typeof(Range<>))
                return new RangeModelBinder();

            return null;
        }
    }
}
